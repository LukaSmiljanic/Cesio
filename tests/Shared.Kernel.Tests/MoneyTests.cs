using System.Globalization;

namespace Shared.Kernel.Tests;

public sealed class MoneyTests
{
    private static Money Rsd(string amount) => Money.Parse(amount, Currency.Rsd);

    private static Money Eur(string amount) => Money.Parse(amount, Currency.Eur);

    [Theory]
    [InlineData("RSD", 2)]
    [InlineData("EUR", 2)]
    public void Currency_allow_list_has_minor_units(string code, int minorUnits)
    {
        var currency = Currency.FromCode(code);

        Assert.Equal(code, currency.Code);
        Assert.Equal(minorUnits, currency.MinorUnits);
        Assert.Equal(code, currency.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rsd")]
    [InlineData("USD")]
    public void Currency_outside_the_allow_list_is_rejected(string? code)
    {
        Assert.False(Currency.TryFromCode(code, out _));
        Assert.Throws<ArgumentException>(() => Currency.FromCode(code));
    }

    [Fact]
    public void Constructor_requires_a_currency() =>
        Assert.Throws<ArgumentNullException>(() => new Money(1m, null!));

    [Fact]
    public void Zero_has_the_currency()
    {
        var zero = Money.Zero(Currency.Eur);

        Assert.Equal(0m, zero.Amount);
        Assert.Equal(Currency.Eur, zero.Currency);
    }

    [Fact]
    public void Add_and_subtract_keep_the_currency_and_do_not_round()
    {
        Assert.Equal(Rsd("100.01"), Rsd("100.00").Add(Rsd("0.01")));
        Assert.Equal(Rsd("-0.01"), Rsd("100.00").Subtract(Rsd("100.01")));
        Assert.Equal(1.0005m, Rsd("1.00").Multiply(1.0005m).Amount);
    }

    [Fact]
    public void Multiply_does_not_round()
    {
        var fee = Rsd("97000.00").Multiply(0.0125m);

        Assert.Equal(1212.5m, fee.Amount);
        Assert.Equal(Currency.Rsd, fee.Currency);
    }

    [Theory]
    [InlineData("0.005", "0.01")]
    [InlineData("-0.005", "-0.01")]
    [InlineData("0.004", "0.00")]
    [InlineData("-0.004", "0.00")]
    [InlineData("1.125", "1.13")]
    [InlineData("-1.125", "-1.13")]
    [InlineData("97000", "97000.00")]
    public void Round_is_half_away_from_zero_to_minor_units(string amount, string expected)
    {
        var rounded = new Money(decimal.Parse(amount, CultureInfo.InvariantCulture), Currency.Rsd).Round();

        Assert.Equal(expected, rounded.FormatAmount());
    }

    [Fact]
    public void Cross_currency_operations_throw()
    {
        Assert.Throws<CurrencyMismatchException>(() => Rsd("1").Add(Eur("1")));
        Assert.Throws<CurrencyMismatchException>(() => Rsd("1").Subtract(Eur("1")));
        Assert.Throws<CurrencyMismatchException>(() => Rsd("1").CompareTo(Eur("1")));
        Assert.Throws<CurrencyMismatchException>(() => Rsd("1") < Eur("1"));
    }

    [Fact]
    public void Operations_reject_null()
    {
        Assert.Throws<ArgumentNullException>(() => Rsd("1").Add(null!));
        Assert.Throws<ArgumentNullException>(() => Rsd("1").CompareTo(null));
        Assert.Throws<ArgumentNullException>(() => null! < Rsd("1"));
    }

    [Fact]
    public void Comparisons_use_the_amount()
    {
        Assert.True(Rsd("1.00") < Rsd("1.01"));
        Assert.True(Rsd("1.01") > Rsd("1.00"));
        Assert.True(Rsd("1.00") <= Rsd("1.0"));
        Assert.True(Rsd("1.0") >= Rsd("1.00"));
        Assert.False(Rsd("2") < Rsd("1"));
        Assert.Equal(0, Rsd("1.00").CompareTo(Rsd("1")));
    }

    [Fact]
    public void Equal_amounts_with_different_scale_are_equal_but_keep_their_scale()
    {
        Assert.Equal(Rsd("97000"), Rsd("97000.00"));
        Assert.Equal(Rsd("97000").GetHashCode(), Rsd("97000.00").GetHashCode());
        Assert.Equal("97000.00", Rsd("97000.00").FormatAmount());
        Assert.Equal("97000", Rsd("97000").FormatAmount());
        Assert.Equal("97000.00 RSD", Rsd("97000.00").ToString());
    }

    [Theory]
    [InlineData("97000.00")]
    [InlineData("97000")]
    [InlineData("0")]
    [InlineData("0.5")]
    [InlineData("-12.34")]
    [InlineData("999999999999999.99")]
    public void Parse_accepts_decimal_strings(string amount) =>
        Assert.Equal(amount, Money.Parse(amount, Currency.Rsd).FormatAmount());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("9.7e4")]
    [InlineData("97000.001")]
    [InlineData("97,000.00")]
    [InlineData("97000,00")]
    [InlineData(" 97000")]
    [InlineData("97000 ")]
    [InlineData("+1")]
    [InlineData("-")]
    [InlineData(".5")]
    [InlineData("5.")]
    [InlineData("1.2.3")]
    [InlineData("1.2x")]
    [InlineData("1000000000000000")]
    [InlineData("١٢٣")]
    [InlineData("１２３")]
    [InlineData("NaN")]
    [InlineData("000000000000000000000000000000001")]
    public void Parse_rejects_anything_but_a_plain_decimal_string(string? amount)
    {
        Assert.False(Money.TryParse(amount, Currency.Rsd, out var money));
        Assert.Null(money);
        Assert.Throws<FormatException>(() => Money.Parse(amount, Currency.Rsd));
    }

    [Fact]
    public void Parse_requires_a_currency() =>
        Assert.Throws<ArgumentNullException>(() => Money.TryParse("1", null!, out _));

    [Fact]
    public void Currency_mismatch_exception_has_the_standard_constructors()
    {
        var inner = new InvalidOperationException();

        Assert.NotNull(new CurrencyMismatchException().Message);
        Assert.Equal("m", new CurrencyMismatchException("m").Message);
        Assert.Same(inner, new CurrencyMismatchException("m", inner).InnerException);
    }
}
