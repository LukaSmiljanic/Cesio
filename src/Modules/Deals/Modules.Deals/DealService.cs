using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Compliance.Contracts;
using Modules.Deals.Contracts;
using Modules.Deals.Persistence;
using Modules.Marketplace.Contracts;
using Modules.Organizations.Contracts;
using Modules.Receivables.Contracts;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Deals;

public sealed record AcceptOfferRequest(AcceptanceAcknowledgements? Acknowledgements);

public sealed record AcceptanceAcknowledgements(bool NetProceedsShown, string? TermsVersion);

/// <summary>
/// Offer acceptance and deal creation (task 014, api.md §3.6): the checks that call other systems run first, then one
/// transaction closes the listing, supersedes the other offers, reserves the receivable and creates the deal (ADR-006).
/// </summary>
internal sealed class DealService(
    DealsDbContext deals,
    IListingWriter listings,
    DealStepService steps,
    IReceivableMarket receivables,
    IReceivableWriter receivableWriter,
    IOrganizationQuery organizations,
    IAuthorizationQuery authorizations,
    IHoldQuery holds,
    IBusinessParameters parameters,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IOutbox outbox,
    IRequestContext request,
    TimeProvider timeProvider)
{
    /// <summary>product-spec.md §parameters: signature deadline, default 5 business days.</summary>
    public const string SignatureDeadlineBusinessDays = "deals.signatureDeadlineBusinessDays";

    private const int DefaultSignatureDeadlineBusinessDays = 5;

    /// <summary>INV-R4 at acceptance.</summary>
    private static readonly TimeSpan SefMaxAge = TimeSpan.FromHours(1);

    private Guid OrganizationId => request.OrganizationId ?? throw new InvalidOperationException("An organization context is required.");

    private Guid UserId => request.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

    public async Task<IResult> AcceptAsync(Guid offerId, AcceptOfferRequest body, int? listingVersion, DateTimeOffset stepUpAt, CancellationToken cancellationToken)
    {
        if (await listings.GetOfferAsync(offerId, cancellationToken) is not { } offer || offer.SellerOrgId != OrganizationId)
        {
            return Problems.Error(StatusCodes.Status404NotFound, "not_found", "The offer does not exist.");
        }

        if (!await organizations.IsSignatoryAsync(OrganizationId, UserId, cancellationToken))
        {
            return Problems.Error(StatusCodes.Status403Forbidden, "signatory_required", "Only a Signatory of the seller can accept an offer (INV-A3).");
        }

        if (body.Acknowledgements is not { NetProceedsShown: true })
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Confirm that the net proceeds were shown.");
        }

        if (listingVersion is { } expected && expected != offer.ListingVersion)
        {
            return Problems.Error(StatusCodes.Status412PreconditionFailed, "concurrency_conflict", "The listing changed in the meantime. Reload it and try again.");
        }

        if (offer.State != OfferState.Active || offer.ValidUntil <= timeProvider.GetUtcNow())
        {
            return Problems.Error(StatusCodes.Status409Conflict, "offer_not_active", "The offer is no longer active.");
        }

        if (offer.ListingState != ListingState.Open)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "listing_not_open", "The listing is no longer open.");
        }

        if (await holds.HasActiveHoldAsync(HoldSubject.Organization, offer.SellerOrgId, cancellationToken)
            || await holds.HasActiveHoldAsync(HoldSubject.Organization, offer.FactorOrgId, cancellationToken)
            || await holds.HasActiveHoldAsync(HoldSubject.Receivable, offer.ReceivableId, cancellationToken))
        {
            return Problems.Error(StatusCodes.Status423Locked, "hold_active", "A hold blocks the acceptance.");
        }

        if (!await authorizations.IsFactorAuthorizedAsync(offer.FactorOrgId, cancellationToken))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "offer_not_active", "The factor is no longer authorized (INV-A2).");
        }

        // INV-R4: re-poll SEF before the transaction; a changed status makes the receivable Ineligible.
        switch (await receivables.EnsureFreshApprovalAsync(offer.ReceivableId, SefMaxAge, cancellationToken))
        {
            case SefApproval.Unavailable:
                return Problems.Error(StatusCodes.Status503ServiceUnavailable, "upstream_unavailable", "SEF cannot be reached to confirm the buyer's approval; try again later.");
            case SefApproval.NotApproved:
                await unitOfWork.ExecuteInTransactionAsync(
                    ct => receivables.ChangeStateAsync(offer.ReceivableId, ReceivableState.Listed, ReceivableState.Ineligible, "sef_status_changed_at_acceptance", ct),
                    cancellationToken);
                return Problems.Error(StatusCodes.Status409Conflict, "receivable_ineligible", "SEF no longer shows the invoice as approved by the buyer.");
        }

        var now = timeProvider.GetUtcNow();
        var signatureDays = await parameters.GetAsync(SignatureDeadlineBusinessDays, DefaultSignatureDeadlineBusinessDays, cancellationToken);
        var dealId = SequentialGuid.NewGuid();
        Deal? deal = null;
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await listings.CloseForAcceptanceAsync(offerId, dealId, ct);
                    await receivableWriter.ReserveAsync(offer.ReceivableId, dealId, ct);
                    deal = new Deal
                    {
                        Id = dealId,
                        Reference = await NextReferenceAsync(now, ct),
                        ReceivableId = offer.ReceivableId,
                        ListingId = offer.ListingId,
                        OfferId = offerId,
                        SellerOrgId = offer.SellerOrgId,
                        FactorOrgId = offer.FactorOrgId,
                        FaceValue = offer.FaceValue,
                        PurchasePriceAmount = offer.PurchasePrice.Amount,
                        RecourseType = offer.RecourseType,
                        FundingDeadlineBusinessDays = offer.FundingDeadlineBusinessDays,
                        ConditionsText = offer.ConditionsText,
                        FeeScheduleVersion = offer.FeeScheduleVersion,
                        PlatformFeeAmount = offer.PlatformFee.Amount,
                        SellerFeeAmount = offer.SellerFee.Amount,
                        VatPending = offer.VatPending,
                        State = DealState.AwaitingFactorCdd,
                        SignatureDeadline = SignatureDeadline(now, signatureDays),
                        AcceptedBy = UserId,
                        AcceptedAt = now,
                        AcceptedStepUpAt = stepUpAt,
                    };
                    deals.Deals.Add(deal);
                    deals.StateHistory.Add(new DealStateChange
                    {
                        Id = SequentialGuid.NewGuid(),
                        DealId = dealId,
                        ToState = DealState.AwaitingFactorCdd,
                        ActorUserId = UserId,
                        At = now,
                        Reason = "offer_accepted",
                    });
                    await deals.SaveChangesAsync(ct);
                    await audit.WriteAsync(new AuditEntry("Deal.Created", "Deal", dealId, offer.SellerOrgId, NewState: nameof(DealState.AwaitingFactorCdd),
                        Related: [new AuditReference("Offer", offerId), new AuditReference("Receivable", offer.ReceivableId)],
                        Changes: new Dictionary<string, string?>
                        {
                            ["reference"] = deal.Reference,
                            ["purchasePrice"] = offer.PurchasePrice.ToString(),
                            ["stepUpAt"] = stepUpAt.ToString("O", CultureInfo.InvariantCulture),
                            ["termsVersion"] = body.Acknowledgements.TermsVersion,
                        }), ct);
                    await outbox.AddAsync(new DealCreated(dealId, deal.Reference, offer.ReceivableId, offer.SellerOrgId, offer.FactorOrgId), ct);
                },
                cancellationToken);
        }
        catch (DomainConflictException conflict)
        {
            return Problems.Error(conflict.Status, conflict.ErrorCode, conflict.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.Error(StatusCodes.Status412PreconditionFailed, "concurrency_conflict", "Another request changed the listing or the receivable first.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // The filtered unique indexes are the last line of defence (INV-R1, one accepted offer per listing).
            return Problems.Error(StatusCodes.Status409Conflict, "listing_not_open", "Another offer of this listing was accepted first.");
        }

        return Results.Created($"/api/v1/deals/{dealId}", new { dealId, reference = deal!.Reference });
    }

    public async Task<IResult> ListAsync(CancellationToken cancellationToken)
    {
        var mine = await deals.Deals.AsNoTracking()
            .Where(d => d.SellerOrgId == OrganizationId || d.FactorOrgId == OrganizationId)
            .OrderByDescending(d => d.AcceptedAt).Take(200).ToListAsync(cancellationToken);
        return Results.Ok(mine.Select(View));
    }

    /// <summary>The deal with the state of its steps and whose turn it is (tasks 014, 015).</summary>
    public async Task<IResult> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await PartyDealAsync(id, cancellationToken) is { } deal
            ? Results.Ok(new { deal = View(deal), steps = await steps.StepsAsync(deal.Id, deal.SellerOrgId, deal.FactorOrgId, deal.State, cancellationToken), myRole = deal.SellerOrgId == OrganizationId ? "Seller" : "Factor" })
            : NotFound();

    public async Task<IResult> TimelineAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await PartyDealAsync(id, cancellationToken) is null)
        {
            return NotFound();
        }

        var history = await deals.StateHistory.AsNoTracking().Where(h => h.DealId == id).OrderBy(h => h.At).ToListAsync(cancellationToken);
        return Results.Ok(history.Select(h => new { fromState = h.FromState, toState = h.ToState, at = h.At, reason = h.Reason }));
    }

    /// <summary>The deal if the request's organization is its seller or its factor; anyone else gets 404.</summary>
    private Task<Deal?> PartyDealAsync(Guid id, CancellationToken cancellationToken) =>
        deals.Deals.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && (d.SellerOrgId == OrganizationId || d.FactorOrgId == OrganizationId), cancellationToken);

    /// <summary><c>DL-YYYY-NNNNNN</c> from a database sequence (never reused, even after a rollback).</summary>
    private async Task<string> NextReferenceAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = unitOfWork.Connection.CreateCommand();
        command.Transaction = unitOfWork.RequiredTransaction;
        command.CommandText = $"SELECT NEXT VALUE FOR {DealsDbContext.SchemaName}.{DealsDbContext.ReferenceSequence}";
        var next = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        return string.Create(CultureInfo.InvariantCulture, $"DL-{now.Year}-{next:D6}");
    }

    /// <summary>The end of the business day that is <paramref name="days"/> business days after acceptance (UTC).</summary>
    private static DateTimeOffset SignatureDeadline(DateTimeOffset now, int days) =>
        new(BusinessDays.Add(DateOnly.FromDateTime(now.UtcDateTime), days).ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero);

    private static object View(Deal deal)
    {
        var currency = deal.FaceValue.Currency;
        return new
        {
            id = deal.Id,
            reference = deal.Reference,
            state = deal.State,
            receivableId = deal.ReceivableId,
            listingId = deal.ListingId,
            offerId = deal.OfferId,
            sellerOrgId = deal.SellerOrgId,
            factorOrgId = deal.FactorOrgId,
            faceValue = deal.FaceValue.Round(),
            purchasePrice = new Money(deal.PurchasePriceAmount, currency).Round(),
            recourseType = deal.RecourseType,
            fundingDeadlineBusinessDays = deal.FundingDeadlineBusinessDays,
            conditions = deal.ConditionsText,
            feeScheduleVersion = deal.FeeScheduleVersion,
            platformFee = new Money(deal.PlatformFeeAmount, currency).Round(),
            sellerFee = new Money(deal.SellerFeeAmount, currency).Round(),
            sellerNetProceeds = new Money(deal.PurchasePriceAmount - deal.SellerFeeAmount, currency).Round(),
            signatureDeadline = deal.SignatureDeadline,
            fundingDeadline = deal.FundingDeadline,
            acceptedAt = deal.AcceptedAt,
            agreedContractSha256 = deal.AgreedContractSha256,
            version = deal.Version,
        };
    }

    private static IResult NotFound() => Problems.Error(StatusCodes.Status404NotFound, "not_found", "The deal does not exist.");
}

internal static class Problems
{
    public static IResult Error(int status, string errorCode, string detail) =>
        Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });
}
