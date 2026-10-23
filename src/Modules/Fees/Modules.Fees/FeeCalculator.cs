using Modules.Fees.Contracts;
using Shared.Kernel;

namespace Modules.Fees;

/// <summary>
/// The platform fee from an offer's discount (ADR-007, financial-model.md §1): <c>fee = D × share</c>, at least the
/// minimum and at most the maximum if set, never more than <c>D</c>; rounded half away from zero to minor units once
/// (ADR-008). A split gives the seller its rounded part and the factor the rest, so the parts add up to the total.
/// </summary>
internal sealed class FeeCalculator : IFeeCalculator
{
    private const decimal StandardVatRate = 0.20m;

    public FeeQuote Calculate(Money faceValue, Money purchasePrice, FeeSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(faceValue);
        ArgumentNullException.ThrowIfNull(purchasePrice);
        ArgumentNullException.ThrowIfNull(schedule);
        var definition = schedule.Definition;
        if (faceValue.Currency != definition.Currency)
        {
            throw new CurrencyMismatchException($"Fee schedule {schedule.Version} is in {definition.Currency.Code}, the amount in {faceValue.Currency.Code}.");
        }

        if (purchasePrice.Amount <= 0 || purchasePrice >= faceValue)
        {
            throw new ArgumentException("INV-F1: the purchase price must be above zero and below the face value.", nameof(purchasePrice));
        }

        var discount = faceValue.Subtract(purchasePrice);
        var fee = discount.Multiply(definition.Share);
        if (definition.MinimumFee is { } minimum && fee < minimum)
        {
            fee = minimum;
        }

        if (definition.MaximumFee is { } maximum && fee > maximum)
        {
            fee = maximum;
        }

        if (fee > discount)
        {
            fee = discount;
        }

        var total = fee.Round();
        IEnumerable<(FeePayer Payer, Money Net)> parts = definition.Payer switch
        {
            FeePayer.Factor => [(FeePayer.Factor, total)],
            FeePayer.Seller => [(FeePayer.Seller, total)],
            _ => SplitParts(total, definition.SellerShareOfFee ?? throw new InvalidOperationException("A split schedule needs the seller's share of the fee.")),
        };
        var perPayer = parts.Select(p => new PayerFee(p.Payer, p.Net, Vat(p.Net, definition.VatTreatment))).ToList();
        return new FeeQuote(discount.Round(), perPayer, schedule.Version, definition.VatTreatment == VatTreatment.TBD);
    }

    private static (FeePayer, Money)[] SplitParts(Money total, decimal sellerShare)
    {
        var seller = total.Multiply(sellerShare).Round();
        return [(FeePayer.Seller, seller), (FeePayer.Factor, total.Subtract(seller).Round())];
    }

    private static Money Vat(Money net, VatTreatment treatment) =>
        treatment == VatTreatment.Standard20 ? net.Multiply(StandardVatRate).Round() : Money.Zero(net.Currency).Round();
}
