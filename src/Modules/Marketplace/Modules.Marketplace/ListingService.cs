using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Compliance.Contracts;
using Modules.Documents.Contracts;
using Modules.Marketplace.Contracts;
using Modules.Marketplace.Persistence;
using Modules.Organizations.Contracts;
using Modules.Receivables.Contracts;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Marketplace;

public sealed record AudienceRequest(AudienceType Type, IReadOnlyList<Guid>? FactorOrgIds);

public sealed record OpenListingRequest(DateTimeOffset ClosesAt, AudienceRequest? Audience, IReadOnlyList<Guid>? ExcludedFactorOrgIds, Money? ReservePrice);

public sealed record ListingActionRequest(string? Reason);

/// <summary>Feed filters (api.md §3.4); amounts are RSD, days are days to maturity.</summary>
public sealed record FeedQuery(string? Cursor, int? Limit, decimal? MinAmount, decimal? MaxAmount, int? MinDays, int? MaxDays, string? DebtorPib, string? Sort);

/// <summary>Listings (task 012): sellers open, read and withdraw them, factors browse what they may see, operations suspend.</summary>
internal sealed class ListingService(
    MarketplaceDbContext marketplace,
    ListingVisibility visibility,
    IReceivableMarket receivables,
    IOrganizationQuery organizations,
    IHoldQuery holds,
    IDocumentQuery documents,
    IBusinessParameters parameters,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IAccessLog accessLog,
    IOutbox outbox,
    IRequestContext request,
    TimeProvider timeProvider)
{
    /// <summary>Shared with the Receivables checks (product-spec.md §parameters, default 15).</summary>
    public const string MinDaysAtListing = "receivables.minDaysAtListing";

    /// <summary>Listing maximum duration (product-spec.md §parameters); the docs give no value, 30 days is a working default.</summary>
    public const string MaxListingDays = "marketplace.maxListingDays";

    /// <summary>ADR-007: the factor pays this share of the discount.</summary>
    public const string FeeDiscountShare = "fees.discountShare";

    private const int DefaultMinDaysAtListing = 15;
    private const int DefaultMaxListingDays = 30;
    private const decimal DefaultFeeDiscountShare = 0.15m;
    private const int MaxSelectedFactors = 200;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    /// <summary>INV-R4 at listing.</summary>
    private static readonly TimeSpan SefMaxAge = TimeSpan.FromHours(24);

    private Guid OrganizationId => request.OrganizationId ?? throw new InvalidOperationException("An organization context is required.");

    public async Task<IResult> OpenAsync(Guid receivableId, OpenListingRequest body, CancellationToken cancellationToken)
    {
        if (await receivables.GetAsync(receivableId, cancellationToken) is not { } receivable || receivable.SellerOrgId != OrganizationId)
        {
            return Problems.Error(StatusCodes.Status404NotFound, "not_found", "The receivable does not exist.");
        }

        if (receivable.State != ReceivableState.Verified)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "receivable_not_verified", "Only a Verified receivable can be listed.");
        }

        if (await organizations.GetAsync(OrganizationId, cancellationToken) is not { IsApproved: true } seller
            || await holds.HasActiveHoldAsync(HoldSubject.Organization, OrganizationId, cancellationToken)
            || await holds.HasActiveHoldAsync(HoldSubject.Receivable, receivableId, cancellationToken))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "organization_not_approved", "Publishing listings requires an Approved seller without a hold.");
        }

        var now = timeProvider.GetUtcNow();
        var minDays = await parameters.GetAsync(MinDaysAtListing, DefaultMinDaysAtListing, cancellationToken);
        var maxDays = await parameters.GetAsync(MaxListingDays, DefaultMaxListingDays, cancellationToken);
        var latestClose = new DateTimeOffset(receivable.MaturityDate.AddDays(-minDays).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        if (body.ClosesAt > latestClose)
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "maturity_too_close",
                string.Create(CultureInfo.InvariantCulture, $"The listing must close at least {minDays} days before maturity ({latestClose:yyyy-MM-dd})."));
        }

        if (body.ClosesAt <= now || body.ClosesAt > now.AddDays(maxDays))
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed",
                string.Create(CultureInfo.InvariantCulture, $"closesAt must be in the future and at most {maxDays} days from now."));
        }

        if (await AudienceErrorAsync(body, cancellationToken) is { } audienceError)
        {
            return audienceError;
        }

        if (body.ReservePrice is { } reserve && (reserve.Currency != receivable.FaceValue.Currency || reserve.Amount <= 0 || reserve >= receivable.FaceValue))
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "The reserve price must be positive, in the receivable's currency and below its face value.");
        }

        switch (await receivables.EnsureFreshApprovalAsync(receivableId, SefMaxAge, cancellationToken))
        {
            case SefApproval.Unavailable:
                return Problems.Error(StatusCodes.Status503ServiceUnavailable, "upstream_unavailable", "SEF cannot be reached to confirm the buyer's approval; try again later.");
            case SefApproval.NotApproved:
                return Problems.Error(StatusCodes.Status409Conflict, "sef_not_approved", "SEF does not currently show the invoice as approved by the buyer.");
        }

        var audienceType = body.Audience?.Type ?? AudienceType.AllVerifiedFactors;
        var listing = new Listing
        {
            Id = SequentialGuid.NewGuid(),
            ReceivableId = receivableId,
            SellerOrgId = OrganizationId,
            State = ListingState.Open,
            OpensAt = now,
            ClosesAt = body.ClosesAt,
            AudienceType = audienceType,
            FaceValue = receivable.FaceValue,
            ReservePriceAmount = body.ReservePrice?.Amount,
            MaturityDate = receivable.MaturityDate,
            SellerPib = seller.Pib,
            DebtorPib = receivable.DebtorPib,
        };
        marketplace.Listings.Add(listing);
        if (audienceType == AudienceType.Selected)
        {
            marketplace.ListingAudience.AddRange(body.Audience!.FactorOrgIds!.Distinct()
                .Select(id => new ListingAudience { ListingId = listing.Id, FactorOrgId = id, Mode = AudienceMode.Include }));
        }

        marketplace.ListingAudience.AddRange((body.ExcludedFactorOrgIds ?? []).Distinct()
            .Select(id => new ListingAudience { ListingId = listing.Id, FactorOrgId = id, Mode = AudienceMode.Exclude }));

        try
        {
            var listed = false;
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    listed = await receivables.ChangeStateAsync(receivableId, ReceivableState.Verified, ReceivableState.Listed, "listing_opened", ct);
                    if (!listed)
                    {
                        return;
                    }

                    await marketplace.SaveChangesAsync(ct);
                    await audit.WriteAsync(new AuditEntry("Listing.Opened", "Listing", listing.Id, OrganizationId,
                        NewState: nameof(ListingState.Open), Related: [new AuditReference("Receivable", receivableId)]), ct);
                    await outbox.AddAsync(new ListingOpened(listing.Id, receivableId, OrganizationId), ct);
                },
                cancellationToken);
            if (!listed)
            {
                return ListingAlreadyOpen();
            }
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A parallel request listed the receivable first (INV-R2).
            return ListingAlreadyOpen();
        }

        return Results.Created($"/api/v1/listings/{listing.Id}", SellerView(listing, []));
    }

    public async Task<IResult> MineAsync(CancellationToken cancellationToken)
    {
        var listings = await marketplace.Listings.AsNoTracking().Where(l => l.SellerOrgId == OrganizationId)
            .OrderByDescending(l => l.OpensAt).Take(200).ToListAsync(cancellationToken);
        return Results.Ok(listings.Select(l => SellerView(l, null)));
    }

    public async Task<IResult> GetMineAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await marketplace.Listings.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id && l.SellerOrgId == OrganizationId, cancellationToken) is not { } listing)
        {
            return NotFound();
        }

        var audience = await marketplace.ListingAudience.AsNoTracking().Where(a => a.ListingId == id).ToListAsync(cancellationToken);
        return Results.Ok(SellerView(listing, audience));
    }

    /// <summary>The seller withdraws an Open or Suspended listing (no accepted offer exists then: acceptance closes it).</summary>
    public async Task<IResult> WithdrawAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await marketplace.Listings.SingleOrDefaultAsync(l => l.Id == id && l.SellerOrgId == OrganizationId, cancellationToken) is not { } listing)
        {
            return NotFound();
        }

        if (listing.State is not (ListingState.Open or ListingState.Suspended))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Only an Open or Suspended listing can be withdrawn.");
        }

        var previous = listing.State;
        listing.State = ListingState.Withdrawn;
        return await CommitAsync(
            async ct =>
            {
                await receivables.ChangeStateAsync(listing.ReceivableId, ReceivableState.Listed, ReceivableState.Verified, "listing_withdrawn", ct);
                await audit.WriteAsync(new AuditEntry("Listing.Withdrawn", "Listing", id, listing.SellerOrgId,
                    PreviousState: previous.ToString(), NewState: nameof(ListingState.Withdrawn)), ct);
                await outbox.AddAsync(new ListingWithdrawn(id, listing.ReceivableId, listing.SellerOrgId), ct);
            },
            cancellationToken)
            ? Results.Ok(new { state = listing.State })
            : Conflict();
    }

    /// <summary>Operations suspends an Open listing (it disappears from the feed) or resumes a Suspended one, with a reason.</summary>
    public async Task<IResult> SuspendOrResumeAsync(Guid id, bool suspend, ListingActionRequest body, CancellationToken cancellationToken)
    {
        if (await marketplace.Listings.SingleOrDefaultAsync(l => l.Id == id, cancellationToken) is not { } listing)
        {
            return NotFound();
        }

        var reason = body.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 400)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Give a reason of at most 400 characters.");
        }

        var from = suspend ? ListingState.Open : ListingState.Suspended;
        if (listing.State != from)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", $"Only a {from} listing can be {(suspend ? "suspended" : "resumed")}.");
        }

        if (!suspend && listing.ClosesAt <= timeProvider.GetUtcNow())
        {
            return Problems.Error(StatusCodes.Status409Conflict, "listing_closed", "The listing's closing time has passed; it cannot be resumed.");
        }

        listing.State = suspend ? ListingState.Suspended : ListingState.Open;
        listing.SuspendedReason = suspend ? reason : null;
        return await CommitAsync(
            ct => audit.WriteAsync(new AuditEntry(suspend ? "Listing.Suspended" : "Listing.Resumed", "Listing", id, listing.SellerOrgId,
                PreviousState: from.ToString(), NewState: listing.State.ToString(), Reason: reason), ct),
            cancellationToken)
            ? Results.Ok(new { state = listing.State })
            : Conflict();
    }

    /// <summary>The factor feed: visible listings only, ordered by closing time or amount (then id), paged by an opaque cursor.</summary>
    public async Task<IResult> FeedAsync(FeedQuery query, CancellationToken cancellationToken)
    {
        if (await visibility.ViewerAsync(cancellationToken) is not { } viewer)
        {
            return Problems.Error(StatusCodes.Status403Forbidden, "factor_not_authorized", "The marketplace is open to Approved, authorized factors.");
        }

        var byAmount = string.Equals(query.Sort, "amount", StringComparison.OrdinalIgnoreCase);
        if ((query.Sort is not null && !byAmount && !string.Equals(query.Sort, "closesAt", StringComparison.OrdinalIgnoreCase))
            || query.Limit is < 1 or > MaxPageSize
            || (query.DebtorPib is not null && !Pib.TryParse(query.DebtorPib, out _)))
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed",
                $"sort is closesAt or amount, limit is 1–{MaxPageSize}, debtorPib is a valid PIB.");
        }

        var listings = visibility.Visible(viewer);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (query.MinAmount is { } minAmount)
        {
            listings = listings.Where(l => l.FaceValue.Amount >= minAmount);
        }

        if (query.MaxAmount is { } maxAmount)
        {
            listings = listings.Where(l => l.FaceValue.Amount <= maxAmount);
        }

        if (query.MinDays is { } minDays)
        {
            var from = today.AddDays(minDays);
            listings = listings.Where(l => l.MaturityDate >= from);
        }

        if (query.MaxDays is { } maxDays)
        {
            var to = today.AddDays(maxDays);
            listings = listings.Where(l => l.MaturityDate <= to);
        }

        if (query.DebtorPib is { } debtorPib)
        {
            listings = listings.Where(l => l.DebtorPib == debtorPib);
        }

        var offset = 0;
        if (query.Cursor is not null)
        {
            if (DecodeCursor(query.Cursor) is not { } decoded)
            {
                return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "The cursor is not valid.");
            }

            offset = decoded;
        }

        var limit = query.Limit ?? DefaultPageSize;
        var ordered = byAmount ? listings.OrderBy(l => l.FaceValue.Amount).ThenBy(l => l.Id) : listings.OrderBy(l => l.ClosesAt).ThenBy(l => l.Id);
        var page = await ordered.Skip(offset).Take(limit + 1).ToListAsync(cancellationToken);
        var items = page.Take(limit).ToList();
        return Results.Ok(new
        {
            items = items.Select(l => new
            {
                id = l.Id,
                faceValue = l.FaceValue.Round(),
                maturityDate = l.MaturityDate,
                daysToMaturity = l.MaturityDate.DayNumber - today.DayNumber,
                debtorPib = l.DebtorPib,
                closesAt = l.ClosesAt,
            }),
            nextCursor = page.Count > limit ? EncodeCursor(offset + limit) : null,
        });
    }

    /// <summary>The listing detail for a factor that may see it (api.md §3.4); never the reserve price or other offers.</summary>
    public async Task<IResult> DetailAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await visibility.VisibleListingAsync(id, cancellationToken) is not { } visible)
        {
            return NotFound();
        }

        var listing = visible.Listing;
        var receivable = (await receivables.GetAsync(listing.ReceivableId, cancellationToken))!;
        var seller = await organizations.GetAsync(listing.SellerOrgId, cancellationToken);
        var attachments = new List<object>();
        foreach (var documentId in receivable.EvidenceDocumentIds.Prepend(receivable.UblDocumentId))
        {
            if (await documents.GetAsync(documentId, cancellationToken) is { ScanStatus: ScanStatus.Clean } document)
            {
                attachments.Add(new { id = document.Id, purpose = document.Purpose, fileName = document.FileName, contentType = document.ContentType, sizeBytes = document.SizeBytes });
            }
        }

        await unitOfWork.ExecuteInTransactionAsync(
            ct => accessLog.RecordAsync(new AccessLogEntry("Listing.Viewed", "Listing", id, visible.Viewer.OrganizationId), ct),
            cancellationToken);
        return Results.Ok(new
        {
            id = listing.Id,
            state = listing.State,
            closesAt = listing.ClosesAt,
            faceValue = listing.FaceValue.Round(),
            debtor = new { legalName = receivable.DebtorLegalName, pib = receivable.DebtorPib, mb = receivable.DebtorMb },
            invoice = new
            {
                number = receivable.InvoiceNumber,
                issueDate = receivable.IssueDate,
                dueDate = receivable.DueDate,
                netAmount = receivable.NetAmount.Round(),
                vatAmount = receivable.VatAmount.Round(),
                grossAmount = receivable.GrossAmount.Round(),
            },
            sef = new { status = receivable.SefStatus, checkedAt = receivable.SefStatusCheckedAt },
            seller = new { legalName = seller?.LegalName, kybStatus = seller?.Status },
            documents = attachments,
            ownOffer = (object?)null,
            platformFee = new
            {
                basis = "Discount",
                share = (await parameters.GetAsync(FeeDiscountShare, DefaultFeeDiscountShare, cancellationToken)).ToString(CultureInfo.InvariantCulture),
                payer = "Factor",
            },
        });
    }

    private async Task<IResult?> AudienceErrorAsync(OpenListingRequest body, CancellationToken cancellationToken)
    {
        var selected = body.Audience is { Type: AudienceType.Selected } ? body.Audience.FactorOrgIds ?? [] : [];
        var excluded = body.ExcludedFactorOrgIds ?? [];
        if ((body.Audience is { Type: AudienceType.Selected } && selected.Count == 0)
            || (body.Audience is { Type: AudienceType.AllVerifiedFactors } && body.Audience.FactorOrgIds is { Count: > 0 })
            || selected.Count + excluded.Count > MaxSelectedFactors
            || selected.Intersect(excluded).Any())
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed",
                $"A Selected audience names 1–{MaxSelectedFactors} factor organizations, none of them excluded; AllVerifiedFactors names none.");
        }

        foreach (var factorOrgId in selected.Concat(excluded).Distinct())
        {
            if (await organizations.GetAsync(factorOrgId, cancellationToken) is not { Type: OrganizationType.Factor })
            {
                return Problems.Error(StatusCodes.Status422UnprocessableEntity, "unknown_factor", $"{factorOrgId} is not a factor organization.");
            }
        }

        return null;
    }

    private async Task<bool> CommitAsync(Func<CancellationToken, Task> audited, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await marketplace.SaveChangesAsync(ct);
                    await audited(ct);
                },
                cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            marketplace.ChangeTracker.Clear();
            return false;
        }
    }

    private static object SellerView(Listing listing, List<ListingAudience>? audience) => new
    {
        id = listing.Id,
        receivableId = listing.ReceivableId,
        state = listing.State,
        opensAt = listing.OpensAt,
        closesAt = listing.ClosesAt,
        audienceType = listing.AudienceType,
        factorOrgIds = audience?.Where(a => a.Mode == AudienceMode.Include).Select(a => a.FactorOrgId),
        excludedFactorOrgIds = audience?.Where(a => a.Mode == AudienceMode.Exclude).Select(a => a.FactorOrgId),
        faceValue = listing.FaceValue.Round(),
        reservePrice = listing.ReservePriceAmount is { } reserve ? new Money(reserve, listing.FaceValue.Currency).Round() : null,
        maturityDate = listing.MaturityDate,
        suspendedReason = listing.SuspendedReason,
    };

    /// <summary>An opaque page cursor (the position in the ordered feed).</summary>
    private static string EncodeCursor(int offset) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.ToString(CultureInfo.InvariantCulture)));

    private static int? DecodeCursor(string cursor)
    {
        try
        {
            return int.TryParse(Encoding.UTF8.GetString(Convert.FromBase64String(cursor)), NumberStyles.None, CultureInfo.InvariantCulture, out var offset) ? offset : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static IResult NotFound() => Problems.Error(StatusCodes.Status404NotFound, "not_found", "The listing does not exist.");

    private static IResult ListingAlreadyOpen() =>
        Problems.Error(StatusCodes.Status409Conflict, "listing_already_open", "The receivable already has a live listing.");

    private static IResult Conflict() =>
        Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Another request changed the listing first. Reload it and try again.");
}

internal static class Problems
{
    public static IResult Error(int status, string errorCode, string detail) =>
        Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });
}
