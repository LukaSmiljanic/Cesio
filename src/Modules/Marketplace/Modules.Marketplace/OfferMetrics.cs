using Shared.Kernel;

namespace Modules.Marketplace;

/// <summary>What the seller compares (task 013, financial-model.md §1–2). The cost is indicative only (LQ-04).</summary>
/// <param name="DiscountPercent">D / F × 100, rounded to 2 decimals.</param>
/// <param name="IndicativeAnnualCostPercent">Null when the funding date is not before maturity.</param>
internal sealed record OfferMetricValues(Money Discount, decimal DiscountPercent, Money SellerNetProceeds, int TenorDays, decimal? IndicativeAnnualCostPercent);

internal static class OfferMetrics
{
    private const int DaysPerYear = 365;

    /// <summary>
    /// <c>D = F − P</c>; the seller receives <c>P − PF_seller</c>; the cost is <c>(D/P) × 365/t</c>, or
    /// <c>((D + PF)/(P − PF)) × 365/t</c> when the seller pays the fee <c>PF</c>; <c>t</c> runs from the expected funding
    /// date (today + the funding deadline in business days) to maturity (actual/365, display only).
    /// </summary>
    public static OfferMetricValues Compute(Money faceValue, Money purchasePrice, Money sellerFee, DateOnly maturity, DateOnly today, int fundingDeadlineBusinessDays)
    {
        var discount = faceValue.Subtract(purchasePrice);
        var net = purchasePrice.Subtract(sellerFee);
        var tenor = maturity.DayNumber - BusinessDays.Add(today, fundingDeadlineBusinessDays).DayNumber;
        decimal? cost = tenor > 0 && net.Amount > 0
            ? decimal.Round(discount.Add(sellerFee).Amount / net.Amount * DaysPerYear / tenor * 100m, 2, MidpointRounding.AwayFromZero)
            : null;
        return new OfferMetricValues(
            discount.Round(),
            decimal.Round(discount.Amount / faceValue.Amount * 100m, 2, MidpointRounding.AwayFromZero),
            net.Round(),
            tenor,
            cost);
    }
}
