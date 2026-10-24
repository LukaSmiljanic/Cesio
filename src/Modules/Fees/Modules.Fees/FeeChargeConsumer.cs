using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Deals.Contracts;
using Modules.Fees.Contracts;
using Modules.Fees.Persistence;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Kernel;

namespace Modules.Fees;

/// <summary>
/// INV-F3 (task 018): a funded deal gets exactly one Due charge per payer, from the deal's fee snapshot. A repeated
/// delivery of the event finds the charge already there (and the filtered unique index is the last line of defence).
/// </summary>
internal sealed class FeeChargeConsumer(
    FeesDbContext fees,
    IFeeScheduleQuery schedules,
    IBusinessParameters parameters,
    IAuditWriter audit,
    TimeProvider timeProvider) : IIntegrationEventConsumer<DealFunded>
{
    public const string ConsumerName = "fees.charges";

    /// <summary>Days after the statement period's end until a charge is due; the docs give no value, 15 is a working default.</summary>
    public const string PaymentTermsDays = "fees.paymentTermsDays";

    private const int DefaultPaymentTermsDays = 15;

    /// <summary>The rate of <c>VatTreatment.Standard20</c> (financial-model.md §4); whether and how VAT applies is LQ-43.</summary>
    private const decimal StandardVatRate = 0.20m;

    public async Task HandleAsync(DealFunded integrationEvent, Guid eventId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var schedule = await schedules.GetAsync(integrationEvent.FeeScheduleVersion, cancellationToken)
            ?? throw new InvalidOperationException($"Fee schedule {integrationEvent.FeeScheduleVersion} does not exist.");
        var currency = schedule.Definition.Currency;
        if (integrationEvent.PlatformFee.Currency != currency || integrationEvent.SellerFee.Currency != currency || integrationEvent.FaceValue.Currency != currency)
        {
            throw new CurrencyMismatchException($"DealFunded {integrationEvent.DealId} is not in the currency of fee schedule {schedule.Version}.");
        }

        var funded = DateOnly.FromDateTime(integrationEvent.FundedAt.UtcDateTime);
        var periodEnd = new DateOnly(funded.Year, funded.Month, 1).AddMonths(1).AddDays(-1);
        var dueAt = periodEnd.AddDays(await parameters.GetAsync(PaymentTermsDays, DefaultPaymentTermsDays, cancellationToken));
        var factorPart = integrationEvent.PlatformFee.Subtract(integrationEvent.SellerFee).Round();
        var parts = new List<(Guid Payer, string Role, Money Net)>();
        if (factorPart.Amount > 0)
        {
            parts.Add((integrationEvent.FactorOrgId, "Factor", factorPart));
        }

        if (integrationEvent.SellerFee.Amount > 0)
        {
            parts.Add((integrationEvent.SellerOrgId, "Seller", integrationEvent.SellerFee.Round()));
        }

        foreach (var (payer, role, net) in parts)
        {
            if (await fees.FeeCharges.AnyAsync(c => c.DealId == integrationEvent.DealId && c.PayerOrgId == payer && c.Kind == FeeCharge.ChargeKind, cancellationToken))
            {
                continue;
            }

            var vat = schedule.Definition.VatTreatment == VatTreatment.Standard20 ? net.Multiply(StandardVatRate).Round().Amount : 0m;
            var charge = new FeeCharge
            {
                Id = SequentialGuid.NewGuid(),
                DealId = integrationEvent.DealId,
                PayerOrgId = payer,
                PayerRole = role,
                Kind = FeeCharge.ChargeKind,
                Amount = net,
                VatAmount = vat,
                VatPending = schedule.Definition.VatTreatment == VatTreatment.TBD,
                FeeScheduleVersion = schedule.Version,
                Status = "Due",
                DueAt = dueAt,
                DealReference = integrationEvent.Reference,
                InvoiceNumber = integrationEvent.InvoiceNumber,
                FaceValueAmount = integrationEvent.FaceValue.Amount,
                FundedAt = integrationEvent.FundedAt,
                CreatedAt = timeProvider.GetUtcNow(),
            };
            fees.FeeCharges.Add(charge);
            try
            {
                await fees.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
            {
                // A parallel delivery created it first.
                fees.Entry(charge).State = EntityState.Detached;
                continue;
            }

            await audit.WriteAsync(new AuditEntry("FeeCharge.Created", "FeeCharge", charge.Id, payer, NewState: "Due",
                Related: [new AuditReference("Deal", integrationEvent.DealId)],
                Changes: new Dictionary<string, string?> { ["amount"] = net.ToString(), ["vat"] = vat.ToString(System.Globalization.CultureInfo.InvariantCulture), ["eventId"] = eventId.ToString() }), cancellationToken);
        }
    }
}
