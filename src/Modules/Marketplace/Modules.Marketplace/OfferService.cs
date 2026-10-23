using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Fees.Contracts;
using Modules.Marketplace.Contracts;
using Modules.Marketplace.Persistence;
using Modules.Organizations.Contracts;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Marketplace;

public sealed record SubmitOfferRequest(Money? PurchasePrice, RecourseType? RecourseType, int FundingDeadlineBusinessDays, DateTimeOffset ValidUntil, string? Conditions);

public sealed record RejectOfferRequest(string? Reason);

/// <summary>
/// Offers (task 013): authorized factors submit and withdraw immutable offers on listings they see; the seller compares
/// and rejects them. A factor only ever reads its own offers.
/// </summary>
internal sealed class OfferService(
    MarketplaceDbContext marketplace,
    ListingVisibility visibility,
    IOrganizationQuery organizations,
    IFeeScheduleQuery feeSchedules,
    IFeeCalculator feeCalculator,
    IBusinessParameters parameters,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IOutbox outbox,
    IRequestContext request,
    TimeProvider timeProvider)
{
    /// <summary>How long after the listing closes an offer may stay valid for acceptance; 0 = it must be accepted before close.</summary>
    public const string MaxAcceptanceWindowHours = "marketplace.maxAcceptanceWindowHours";

    private const int MaxConditionsLength = 1000;

    private Guid OrganizationId => request.OrganizationId ?? throw new InvalidOperationException("An organization context is required.");

    private Guid UserId => request.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

    public async Task<IResult> SubmitAsync(Guid listingId, SubmitOfferRequest body, CancellationToken cancellationToken)
    {
        if (await visibility.VisibleListingAsync(listingId, cancellationToken) is not { } visible)
        {
            return Problems.Error(StatusCodes.Status404NotFound, "not_found", "The listing does not exist.");
        }

        var listing = visible.Listing;
        if (body.PurchasePrice is not { } price || price.Currency != listing.FaceValue.Currency || price.Amount <= 0 || price >= listing.FaceValue)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "INV-F1: the purchase price is above zero, below the face value and in its currency.");
        }

        var maxWindow = await parameters.GetAsync(MaxAcceptanceWindowHours, 0, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (body.RecourseType is null
            || body.FundingDeadlineBusinessDays is < 1 or > 30
            || body.ValidUntil <= now
            || body.ValidUntil > listing.ClosesAt.AddHours(maxWindow)
            || body.Conditions is { Length: > MaxConditionsLength })
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed",
                $"Give recourseType, fundingDeadlineBusinessDays 1–30, validUntil after now and at most {maxWindow} h after the listing closes, conditions up to {MaxConditionsLength} characters.");
        }

        if (await marketplace.Offers.AnyAsync(o => o.ListingId == listingId && o.FactorOrgId == OrganizationId && o.State == OfferState.Active, cancellationToken))
        {
            return OfferExists();
        }

        var schedule = await feeSchedules.CurrentAsync(now, cancellationToken);
        var quote = feeCalculator.Calculate(listing.FaceValue, price, schedule);
        var offer = new Offer
        {
            Id = SequentialGuid.NewGuid(),
            ListingId = listingId,
            FactorOrgId = OrganizationId,
            PurchasePrice = price,
            RecourseType = body.RecourseType.Value,
            FundingDeadlineBusinessDays = body.FundingDeadlineBusinessDays,
            ValidUntil = body.ValidUntil,
            ConditionsText = string.IsNullOrWhiteSpace(body.Conditions) ? null : body.Conditions.Trim(),
            FeeScheduleVersion = quote.ScheduleVersion,
            PlatformFeeAmount = quote.TotalNet.Amount,
            SellerFeeAmount = quote.PerPayer.Where(p => p.Payer == FeePayer.Seller).Sum(p => p.Net.Amount),
            VatPending = quote.VatPending,
            State = OfferState.Active,
            CreatedBy = UserId,
            CreatedAt = now,
        };
        marketplace.Offers.Add(offer);
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await marketplace.SaveChangesAsync(ct);
                    await audit.WriteAsync(new AuditEntry("Offer.Submitted", "Offer", offer.Id, OrganizationId, NewState: nameof(OfferState.Active),
                        Related: [new AuditReference("Listing", listingId)],
                        Changes: new Dictionary<string, string?>
                        {
                            ["purchasePrice"] = price.ToString(),
                            ["platformFee"] = new Money(offer.PlatformFeeAmount, price.Currency).ToString(),
                            ["feeScheduleVersion"] = offer.FeeScheduleVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        }), ct);
                    await outbox.AddAsync(new OfferSubmitted(offer.Id, listingId, OrganizationId, listing.SellerOrgId), ct);
                },
                cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A parallel submission of the same factor won the filtered unique index.
            return OfferExists();
        }

        return Results.Created($"/api/v1/offers/{offer.Id}", FactorView(offer));
    }

    /// <summary>The fee the factor would pay for a price, before it submits (the offer form's preview).</summary>
    public async Task<IResult> FeePreviewAsync(Guid listingId, string? purchasePrice, CancellationToken cancellationToken)
    {
        if (await visibility.VisibleListingAsync(listingId, cancellationToken) is not { } visible)
        {
            return Problems.Error(StatusCodes.Status404NotFound, "not_found", "The listing does not exist.");
        }

        var faceValue = visible.Listing.FaceValue;
        if (!Money.TryParse(purchasePrice, faceValue.Currency, out var price) || price.Amount <= 0 || price >= faceValue)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "purchasePrice is above zero and below the face value.");
        }

        var quote = feeCalculator.Calculate(faceValue, price, await feeSchedules.CurrentAsync(timeProvider.GetUtcNow(), cancellationToken));
        return Results.Ok(new
        {
            discount = quote.Discount,
            factorFee = quote.PerPayer.Where(p => p.Payer == FeePayer.Factor).Select(p => p.Net).FirstOrDefault() ?? Money.Zero(faceValue.Currency).Round(),
            platformFee = quote.TotalNet,
            vatPending = quote.VatPending,
            feeScheduleVersion = quote.ScheduleVersion,
        });
    }

    public async Task<IResult> WithdrawAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await marketplace.Offers.SingleOrDefaultAsync(o => o.Id == id && o.FactorOrgId == OrganizationId, cancellationToken) is not { } offer)
        {
            return NotFound();
        }

        return await TransitionAsync(offer, OfferState.Withdrawn, "Offer.Withdrawn", reason: null, cancellationToken);
    }

    public async Task<IResult> RejectAsync(Guid id, RejectOfferRequest body, CancellationToken cancellationToken)
    {
        if (await SellersOfferAsync(id, cancellationToken) is not { } offer)
        {
            return NotFound();
        }

        var reason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim();
        if (reason is { Length: > 400 })
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "The reason has at most 400 characters.");
        }

        offer.RejectionReason = reason;
        return await TransitionAsync(offer, OfferState.Rejected, "Offer.Rejected", reason, cancellationToken);
    }

    public async Task<IResult> MineAsync(OfferState? state, CancellationToken cancellationToken)
    {
        var offers = await marketplace.Offers.AsNoTracking()
            .Where(o => o.FactorOrgId == OrganizationId && (state == null || o.State == state))
            .OrderByDescending(o => o.CreatedAt).Take(200).ToListAsync(cancellationToken);
        return Results.Ok(offers.Select(FactorView));
    }

    /// <summary>The factor that made the offer, or the seller of its listing; anyone else gets 404.</summary>
    public async Task<IResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var offer = await marketplace.Offers.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (offer is null)
        {
            return NotFound();
        }

        if (offer.FactorOrgId == OrganizationId)
        {
            return Results.Ok(FactorView(offer));
        }

        var listing = await marketplace.Listings.AsNoTracking().SingleAsync(l => l.Id == offer.ListingId, cancellationToken);
        return listing.SellerOrgId == OrganizationId ? Results.Ok(SellerView(offer, listing, await FactorNameAsync(offer.FactorOrgId, cancellationToken))) : NotFound();
    }

    /// <summary>The seller's comparison of a listing's offers, best net proceeds first.</summary>
    public async Task<IResult> ForListingAsync(Guid listingId, CancellationToken cancellationToken)
    {
        if (await marketplace.Listings.AsNoTracking().SingleOrDefaultAsync(l => l.Id == listingId && l.SellerOrgId == OrganizationId, cancellationToken) is not { } listing)
        {
            return Problems.Error(StatusCodes.Status404NotFound, "not_found", "The listing does not exist.");
        }

        var offers = await marketplace.Offers.AsNoTracking().Where(o => o.ListingId == listingId).ToListAsync(cancellationToken);
        var views = new List<SellerOfferView>();
        foreach (var offer in offers)
        {
            views.Add(SellerView(offer, listing, await FactorNameAsync(offer.FactorOrgId, cancellationToken)));
        }

        return Results.Ok(views
            .OrderByDescending(v => v.State == OfferState.Active).ThenByDescending(v => v.Metrics.SellerNetProceeds.Amount));
    }

    /// <summary>The factor's own Active offer on a listing, for the listing detail.</summary>
    public async Task<object?> OwnActiveOfferAsync(Guid listingId, Guid factorOrgId, CancellationToken cancellationToken) =>
        await marketplace.Offers.AsNoTracking()
            .SingleOrDefaultAsync(o => o.ListingId == listingId && o.FactorOrgId == factorOrgId && o.State == OfferState.Active, cancellationToken) is { } offer
            ? FactorView(offer)
            : null;

    /// <summary>
    /// The listing ended (withdrawn by the seller): its Active offers become Superseded, inside the caller's transaction
    /// (product-spec.md §7.3).
    /// </summary>
    public async Task SupersedeActiveAsync(Guid listingId, CancellationToken cancellationToken)
    {
        var active = await marketplace.Offers.Where(o => o.ListingId == listingId && o.State == OfferState.Active).ToListAsync(cancellationToken);
        foreach (var offer in active)
        {
            offer.State = OfferState.Superseded;
        }

        await marketplace.SaveChangesAsync(cancellationToken);
        foreach (var offer in active)
        {
            await audit.WriteAsync(new AuditEntry("Offer.Superseded", "Offer", offer.Id, offer.FactorOrgId,
                PreviousState: nameof(OfferState.Active), NewState: nameof(OfferState.Superseded), Related: [new AuditReference("Listing", listingId)]), cancellationToken);
        }
    }

    private async Task<Offer?> SellersOfferAsync(Guid id, CancellationToken cancellationToken)
    {
        var offer = await marketplace.Offers.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        return offer is not null && await marketplace.Listings.AnyAsync(l => l.Id == offer.ListingId && l.SellerOrgId == OrganizationId, cancellationToken)
            ? offer
            : null;
    }

    private async Task<IResult> TransitionAsync(Offer offer, OfferState to, string action, string? reason, CancellationToken cancellationToken)
    {
        if (offer.State != OfferState.Active)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "offer_not_active", "Only an Active offer can change.");
        }

        offer.State = to;
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await marketplace.SaveChangesAsync(ct);
                    await audit.WriteAsync(new AuditEntry(action, "Offer", offer.Id, OrganizationId,
                        PreviousState: nameof(OfferState.Active), NewState: to.ToString(), Reason: reason, Related: [new AuditReference("Listing", offer.ListingId)]), ct);
                },
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "offer_not_active", "The offer changed in the meantime.");
        }

        return Results.Ok(new { state = offer.State });
    }

    private static object FactorView(Offer offer) => new
    {
        id = offer.Id,
        listingId = offer.ListingId,
        state = offer.State,
        purchasePrice = offer.PurchasePrice.Round(),
        recourseType = offer.RecourseType,
        fundingDeadlineBusinessDays = offer.FundingDeadlineBusinessDays,
        validUntil = offer.ValidUntil,
        conditions = offer.ConditionsText,
        feeScheduleVersion = offer.FeeScheduleVersion,
        platformFee = new Money(offer.PlatformFeeAmount, offer.PurchasePrice.Currency).Round(),
        factorFee = new Money(offer.PlatformFeeAmount - offer.SellerFeeAmount, offer.PurchasePrice.Currency).Round(),
        vatPending = offer.VatPending,
        createdAt = offer.CreatedAt,
    };

    private async Task<string?> FactorNameAsync(Guid factorOrgId, CancellationToken cancellationToken) =>
        (await organizations.GetAsync(factorOrgId, cancellationToken))?.LegalName;

    private SellerOfferView SellerView(Offer offer, Listing listing, string? factorName)
    {
        var currency = offer.PurchasePrice.Currency;
        var metrics = OfferMetrics.Compute(
            listing.FaceValue,
            offer.PurchasePrice,
            new Money(offer.SellerFeeAmount, currency),
            listing.MaturityDate,
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
            offer.FundingDeadlineBusinessDays);
        return new SellerOfferView(
            offer.Id,
            offer.FactorOrgId,
            factorName,
            offer.State,
            offer.PurchasePrice.Round(),
            offer.RecourseType,
            offer.FundingDeadlineBusinessDays,
            offer.ValidUntil,
            offer.ConditionsText,
            new Money(offer.SellerFeeAmount, currency).Round(),
            metrics);
    }

    private static IResult NotFound() => Problems.Error(StatusCodes.Status404NotFound, "not_found", "The offer does not exist.");

    private static IResult OfferExists() =>
        Problems.Error(StatusCodes.Status409Conflict, "offer_exists", "You already have an Active offer on this listing; withdraw it first.");
}

/// <summary>An offer as the seller compares it; the cost is labelled indicative in the UI (LQ-04).</summary>
internal sealed record SellerOfferView(
    Guid Id,
    Guid FactorOrgId,
    string? FactorName,
    OfferState State,
    Money PurchasePrice,
    RecourseType RecourseType,
    int FundingDeadlineBusinessDays,
    DateTimeOffset ValidUntil,
    string? Conditions,
    Money SellerFee,
    OfferMetricValues Metrics);
