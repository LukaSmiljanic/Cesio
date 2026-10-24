using Modules.Fees;
using Modules.Fees.Contracts;
using Shared.Kernel;

namespace Platform.Integration.Tests;

/// <summary>Task 017a: the pure fee calculation, golden examples of financial-model.md §2 in RSD.</summary>
public sealed class FeeCalculatorTests
{
    private static readonly FeeCalculator Calculator = new();

    [Theory]
    [InlineData("10000.00", "9850.00", "150.00", "22.50")]
    [InlineData("100000.00", "97000.00", "3000.00", "450.00")]
    [InlineData("1000000.00", "985000.00", "15000.00", "2250.00")]
    [InlineData("1000000.00", "970000.00", "30000.00", "4500.00")]
    [InlineData("333333.33", "323333.33", "10000.00", "1500.00")]
    [InlineData("100000.00", "99876.67", "123.33", "18.50")]
    [InlineData("100000.00", "99999.99", "0.01", "0.00")]
    public void The_documented_examples_are_reproduced_exactly(string face, string price, string discount, string fee)
    {
        var quote = Calculator.Calculate(Rsd(face), Rsd(price), Schedule());

        Assert.Equal(Rsd(discount), quote.Discount);
        var factor = Assert.Single(quote.PerPayer);
        Assert.Equal(FeePayer.Factor, factor.Payer);
        Assert.Equal(Rsd(fee), factor.Net);
        Assert.Equal(Rsd("0.00"), factor.Vat);
        Assert.True(quote.VatPending);
        Assert.Equal(1, quote.ScheduleVersion);
    }

    [Theory]
    [InlineData("10000.00", "9800.00", "30.00")]
    [InlineData("100000.00", "97000.00", "450.00")]
    public void Euro_amounts_use_a_euro_schedule(string face, string price, string fee)
    {
        var schedule = new FeeSchedule(2, new FeeScheduleDefinition(FeePayer.Factor, FeeBasis.Discount, 0.15m, null, null, null, VatTreatment.TBD, Currency.Eur), DateTimeOffset.UnixEpoch);

        Assert.Equal(Money.Parse(fee, Currency.Eur), Calculator.Calculate(Money.Parse(face, Currency.Eur), Money.Parse(price, Currency.Eur), schedule).TotalNet);
    }

    [Fact]
    public void The_fee_is_rounded_half_away_from_zero()
    {
        // D = 0.03 → 0.0045 → 0.00; D = 0.10 → 0.015 → 0.02
        Assert.Equal(Rsd("0.00"), Calculator.Calculate(Rsd("100.00"), Rsd("99.97"), Schedule()).TotalNet);
        Assert.Equal(Rsd("0.02"), Calculator.Calculate(Rsd("100.00"), Rsd("99.90"), Schedule()).TotalNet);
    }

    [Fact]
    public void A_minimum_fee_applies_but_never_exceeds_the_discount()
    {
        var schedule = Schedule(minimum: Rsd("1000.00"));

        Assert.Equal(Rsd("1000.00"), Calculator.Calculate(Rsd("100000.00"), Rsd("99000.00"), schedule).TotalNet);
        Assert.Equal(Rsd("500.00"), Calculator.Calculate(Rsd("100000.00"), Rsd("99500.00"), schedule).TotalNet);
    }

    [Fact]
    public void A_maximum_fee_caps_the_fee()
    {
        Assert.Equal(Rsd("2000.00"), Calculator.Calculate(Rsd("1000000.00"), Rsd("985000.00"), Schedule(maximum: Rsd("2000.00"))).TotalNet);
    }

    [Fact]
    public void A_split_gives_parts_that_add_up_to_the_total()
    {
        // D = 1000.01 → fee 150.0015 → 150.00; seller third 50.00, factor 100.00
        var quote = Calculator.Calculate(Rsd("100000.00"), Rsd("98999.99"), Schedule(payer: FeePayer.Split, sellerShare: 1m / 3m));

        Assert.Equal(Rsd("150.00"), quote.TotalNet);
        Assert.Equal(Rsd("50.00"), quote.PerPayer.Single(p => p.Payer == FeePayer.Seller).Net);
        Assert.Equal(Rsd("100.00"), quote.PerPayer.Single(p => p.Payer == FeePayer.Factor).Net);
    }

    [Fact]
    public void Standard_vat_is_rounded_separately()
    {
        var quote = Calculator.Calculate(Rsd("10000.00"), Rsd("9850.00"), Schedule(vat: VatTreatment.Standard20));

        Assert.Equal(Rsd("4.50"), quote.PerPayer[0].Vat);
        Assert.False(quote.VatPending);
    }

    [Fact]
    public void Currency_mismatch_and_invalid_prices_throw()
    {
        Assert.Throws<CurrencyMismatchException>(() => Calculator.Calculate(Rsd("100.00"), new Money(90m, Currency.Eur), Schedule()));
        Assert.Throws<CurrencyMismatchException>(() => Calculator.Calculate(new Money(100m, Currency.Eur), new Money(90m, Currency.Eur), Schedule()));
        Assert.Throws<ArgumentException>(() => Calculator.Calculate(Rsd("100.00"), Rsd("100.00"), Schedule()));
        Assert.Throws<ArgumentException>(() => Calculator.Calculate(Rsd("100.00"), Rsd("120.00"), Schedule()));
        Assert.Throws<ArgumentException>(() => Calculator.Calculate(Rsd("100.00"), Rsd("0.00"), Schedule()));
        Assert.Throws<ArgumentException>(() => Calculator.Calculate(Rsd("100.00"), Rsd("-1.00"), Schedule()));
    }

    [Theory]
    [InlineData("F-1", "F-1")]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1", "'+1")]
    [InlineData("-2", "'-2")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("a;b", "\"a;b\"")]
    public void Csv_cells_never_start_a_formula(string value, string expected) =>
        Assert.Equal(expected, FeeChargeService.Csv(value));

    private static Money Rsd(string amount) => Money.Parse(amount, Currency.Rsd);

    private static FeeSchedule Schedule(
        FeePayer payer = FeePayer.Factor,
        decimal? sellerShare = null,
        Money? minimum = null,
        Money? maximum = null,
        VatTreatment vat = VatTreatment.TBD) =>
        new(1, new FeeScheduleDefinition(payer, FeeBasis.Discount, 0.15m, sellerShare, minimum, maximum, vat, Currency.Rsd), DateTimeOffset.UnixEpoch);
}
