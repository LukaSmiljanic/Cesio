using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Modules.Organizations.Contracts;
using Modules.Receivables.Persistence;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Kernel;

namespace Modules.Receivables;

/// <summary>Everything a check needs about one submitted receivable (task 011).</summary>
internal sealed record CheckContext(
    Receivable Receivable,
    Invoice Invoice,
    Debtor Debtor,
    OrganizationSummary Seller,
    bool SellerOnHold,
    string FreshSefStatus,
    bool DeclarationComplete,
    DateOnly Today);

internal sealed record CheckOutcome(CheckResult Result, string? Details = null)
{
    public static readonly CheckOutcome Pass = new(CheckResult.Pass);

    public static CheckOutcome Warn(string details) => new(CheckResult.Warn, details);

    public static CheckOutcome Fail(string details) => new(CheckResult.Fail, details);
}

/// <summary>One verification rule; the pipeline runs them in registration order (task 011 technical requirements).</summary>
internal interface IReceivableCheck
{
    string Code { get; }

    Task<CheckOutcome> RunAsync(CheckContext context, CancellationToken cancellationToken);
}

/// <summary>Thresholds as business parameters (architecture.md §5); the defaults hold until values are decided (BQ-03, LQ-10).</summary>
internal static class ReceivableParameters
{
    public const string MinDaysAtListing = "receivables.minDaysAtListing";
    public const string MaxTenorDays = "receivables.maxTenorDays";
    public const string MinFaceValue = "receivables.minFaceValue";
    public const string MaxFaceValue = "receivables.maxFaceValue";
    public const string AutoApproveMaxFaceValue = "receivables.autoApproveMaxFaceValue";
    public const string WarnOnNewDebtor = "receivables.warnOnNewDebtor";

    /// <summary>product-spec.md §parameters (default 15).</summary>
    public const int DefaultMinDaysAtListing = 15;

    /// <summary>A working default only; LQ-10 stays open.</summary>
    public const int DefaultMaxTenorDays = 180;

    public const decimal DefaultMinFaceValue = 500_000m;
    public const decimal DefaultMaxFaceValue = 200_000_000m;

    /// <summary>0: no automatic approval, every receivable goes to the manual queue (auto-approval is "if configured").</summary>
    public const decimal DefaultAutoApproveMaxFaceValue = 0m;

    /// <summary>The amount parameters are RSD amounts (BQ-03); a receivable in another currency has no limits and never passes.</summary>
    public static readonly Currency LimitCurrency = Currency.Rsd;
}

internal sealed class SefFreshApprovedCheck : IReceivableCheck
{
    public string Code => "SEF_FRESH_APPROVED";

    public Task<CheckOutcome> RunAsync(CheckContext context, CancellationToken cancellationToken) =>
        Task.FromResult(context.FreshSefStatus == "Approved"
            ? CheckOutcome.Pass
            : CheckOutcome.Fail($"SEF status is {context.FreshSefStatus}, not Approved by the buyer."));
}

internal sealed class SellerIsIssuerCheck : IReceivableCheck
{
    public string Code => "SELLER_IS_ISSUER";

    /// <summary>
    /// The import refuses an invoice whose UBL supplier PIB is not the seller's PIB (task 010) and stores the invoice
    /// under that seller; this check confirms the stored link.
    /// </summary>
    public Task<CheckOutcome> RunAsync(CheckContext context, CancellationToken cancellationToken) =>
        Task.FromResult(context.Invoice.SellerOrgId == context.Seller.Id
            ? new CheckOutcome(CheckResult.Pass, "The supplier PIB matched the seller's PIB at import.")
            : CheckOutcome.Fail("The invoice belongs to another organization."));
}

internal sealed class SellerApprovedCheck : IReceivableCheck
{
    public string Code => "SELLER_APPROVED";

    public Task<CheckOutcome> RunAsync(CheckContext context, CancellationToken cancellationToken) =>
        Task.FromResult(!context.Seller.IsApproved
            ? CheckOutcome.Fail($"The seller is {context.Seller.Status}, not Approved.")
            : context.SellerOnHold ? CheckOutcome.Fail("The seller has an active hold.") : CheckOutcome.Pass);
}

internal sealed class NotMaturedCheck(IBusinessParameters parameters) : IReceivableCheck
{
    public string Code => "NOT_MATURED";

    public async Task<CheckOutcome> RunAsync(CheckContext context, CancellationToken cancellationToken)
    {
        var minDays = await parameters.GetAsync(ReceivableParameters.MinDaysAtListing, ReceivableParameters.DefaultMinDaysAtListing, cancellationToken);
        var days = context.Receivable.MaturityDate.DayNumber - context.Today.DayNumber;
        return days >= minDays
            ? CheckOutcome.Pass
            : CheckOutcome.Fail(string.Create(CultureInfo.InvariantCulture, $"{days} days to maturity; at least {minDays} are required."));
    }
}

internal sealed class MaxTenorCheck(IBusinessParameters parameters) : IReceivableCheck
{
    public string Code => "MAX_TENOR";

    /// <summary>The maximum tenor is a parameter pending LQ-10 (task 011).</summary>
    public async Task<CheckOutcome> RunAsync(CheckContext context, CancellationToken cancellationToken)
    {
        var maxDays = await parameters.GetAsync(ReceivableParameters.MaxTenorDays, ReceivableParameters.DefaultMaxTenorDays, cancellationToken);
        var days = context.Receivable.MaturityDate.DayNumber - context.Today.DayNumber;
        return days <= maxDays
            ? CheckOutcome.Pass
            : CheckOutcome.Fail(string.Create(CultureInfo.InvariantCulture, $"{days} days to maturity; at most {maxDays} are allowed."));
    }
}

internal sealed class AmountLimitsCheck(IBusinessParameters parameters) : IReceivableCheck
{
    public string Code => "AMOUNT_LIMITS";

    public async Task<CheckOutcome> RunAsync(CheckContext context, CancellationToken cancellationToken)
    {
        var faceValue = context.Receivable.FaceValue;
        if (faceValue.Currency != ReceivableParameters.LimitCurrency)
        {
            return CheckOutcome.Fail($"No amount limits are set for {faceValue.Currency.Code}.");
        }

        var min = new Money(await parameters.GetAsync(ReceivableParameters.MinFaceValue, ReceivableParameters.DefaultMinFaceValue, cancellationToken), ReceivableParameters.LimitCurrency);
        var max = new Money(await parameters.GetAsync(ReceivableParameters.MaxFaceValue, ReceivableParameters.DefaultMaxFaceValue, cancellationToken), ReceivableParameters.LimitCurrency);
        return faceValue >= min && faceValue <= max ? CheckOutcome.Pass : CheckOutcome.Fail($"The face value must be between {min} and {max}.");
    }
}

internal sealed class FaceValueConsistentCheck : IReceivableCheck
{
    public string Code => "FACE_VALUE_CONSISTENT";

    /// <summary>INV-R5: faceValue ≤ gross − known credit notes (none are known yet; credit notes are not imported).</summary>
    public Task<CheckOutcome> RunAsync(CheckContext context, CancellationToken cancellationToken) =>
        Task.FromResult(context.Receivable.FaceValue <= new Money(context.Invoice.GrossAmount, context.Invoice.Currency)
            ? CheckOutcome.Pass
            : CheckOutcome.Fail("The face value is above the invoice gross amount."));
}

internal sealed class DebtorScreeningCheck : IReceivableCheck
{
    public string Code => "DEBTOR_SCREENING";

    /// <summary>Sanctions screening arrives with task 007 (LQ-30/31); until then a person must look, so it warns.</summary>
    public Task<CheckOutcome> RunAsync(CheckContext context, CancellationToken cancellationToken) =>
        Task.FromResult(CheckOutcome.Warn("Debtor screening is not available yet (task 007); review manually."));
}

internal sealed class DebtorNotSellerCheck : IReceivableCheck
{
    public string Code => "DEBTOR_NOT_SELLER";

    public Task<CheckOutcome> RunAsync(CheckContext context, CancellationToken cancellationToken) =>
        Task.FromResult(context.Debtor.Pib != context.Seller.Pib ? CheckOutcome.Pass : CheckOutcome.Fail("The debtor is the seller itself."));
}

internal sealed class RelatedPartyCheck : IReceivableCheck
{
    public string Code => "RELATED_PARTY";

    /// <summary>Shared representatives or beneficial owners are compared "where known"; that data arrives with task 007.</summary>
    public Task<CheckOutcome> RunAsync(CheckContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new CheckOutcome(CheckResult.Pass, "No representative or ownership data yet (task 007)."));
}

internal sealed class DuplicateFingerprintCheck(ReceivablesDbContext receivables) : IReceivableCheck
{
    public const string CheckCode = "DUPLICATE_FINGERPRINT";

    /// <summary>What the seller sees instead of the result: the check looks at other sellers' receivables.</summary>
    public const string SellerDetails = "Checked by compliance staff.";

    public string Code => CheckCode;

    /// <summary>
    /// INV-R3: the same debtor, amount and dates on a non-rejected receivable of another invoice, across all sellers.
    /// Drafts are left out, so a mere import by someone else cannot push a receivable into the manual queue.
    /// </summary>
    public async Task<CheckOutcome> RunAsync(CheckContext context, CancellationToken cancellationToken)
    {
        var receivable = context.Receivable;
        var duplicate = await receivables.Receivables.IgnoreQueryFilters().AnyAsync(
            r => r.Fingerprint == receivable.Fingerprint && r.InvoiceId != receivable.InvoiceId
                && r.State != Contracts.ReceivableState.Rejected && r.State != Contracts.ReceivableState.Draft,
            cancellationToken);
        return duplicate ? CheckOutcome.Warn("Another receivable has the same debtor, amount and dates.") : CheckOutcome.Pass;
    }
}

internal sealed class NewDebtorForSellerCheck(ReceivablesDbContext receivables, IBusinessParameters parameters) : IReceivableCheck
{
    public string Code => "NEW_DEBTOR_FOR_SELLER";

    /// <summary>Known means an earlier submitted, not rejected or cancelled receivable of this seller on another invoice.</summary>
    public async Task<CheckOutcome> RunAsync(CheckContext context, CancellationToken cancellationToken)
    {
        if (!await parameters.GetAsync(ReceivableParameters.WarnOnNewDebtor, true, cancellationToken))
        {
            return CheckOutcome.Pass;
        }

        var receivable = context.Receivable;
        var known = await receivables.Receivables.IgnoreQueryFilters().AnyAsync(
            r => r.SellerOrgId == receivable.SellerOrgId && r.DebtorId == receivable.DebtorId && r.InvoiceId != receivable.InvoiceId
                && r.State != Contracts.ReceivableState.Draft && r.State != Contracts.ReceivableState.Rejected && r.State != Contracts.ReceivableState.Cancelled,
            cancellationToken);
        return known ? CheckOutcome.Pass : CheckOutcome.Warn("The first receivable of this seller on this debtor.");
    }
}

internal sealed class DeclarationCompleteCheck : IReceivableCheck
{
    public string Code => "DECLARATION_COMPLETE";

    public Task<CheckOutcome> RunAsync(CheckContext context, CancellationToken cancellationToken) =>
        Task.FromResult(context.DeclarationComplete ? CheckOutcome.Pass : CheckOutcome.Fail("The current declaration is not confirmed in full."));
}
