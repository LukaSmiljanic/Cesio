namespace Shared.Kernel.Tests;

/// <summary>Valid samples are generated with the check-digit algorithms; they are not taken from real companies.</summary>
public sealed class IdentifierTests
{
    [Theory]
    [InlineData("100000008")]
    [InlineData("123456788")]
    [InlineData("999999995")]
    [InlineData("102030400")]
    public void Pib_with_a_valid_check_digit_is_accepted(string value)
    {
        var pib = Pib.Parse(value);

        Assert.Equal(value, pib.Value);
        Assert.Equal(value, pib.ToString());
        Assert.Equal(pib, Pib.Parse(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("100000007")]
    [InlineData("123456789")]
    [InlineData("10000000")]
    [InlineData("1000000080")]
    [InlineData("10000000a")]
    [InlineData(" 10000000")]
    [InlineData("١٠٠٠٠٠٠٠٨")]
    public void Invalid_pib_is_rejected(string? value)
    {
        Assert.False(Pib.TryParse(value, out var pib));
        Assert.Null(pib);
        Assert.Throws<FormatException>(() => Pib.Parse(value));
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("07000000")]
    public void Maticni_broj_with_eight_digits_is_accepted(string value)
    {
        var maticniBroj = MaticniBroj.Parse(value);

        Assert.Equal(value, maticniBroj.Value);
        Assert.Equal(value, maticniBroj.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1234567")]
    [InlineData("123456789")]
    [InlineData("1234567a")]
    [InlineData("１２３４５６７８")]
    public void Invalid_maticni_broj_is_rejected(string? value)
    {
        Assert.False(MaticniBroj.TryParse(value, out var maticniBroj));
        Assert.Null(maticniBroj);
        Assert.Throws<FormatException>(() => MaticniBroj.Parse(value));
    }

    [Theory]
    [InlineData("160000000000510005", "160000000000510005")]
    [InlineData("160-5100-05", "160000000000510005")]
    [InlineData("160-0000000005100-05", "160000000000510005")]
    [InlineData("205-123456789-68", "205000012345678968")]
    [InlineData("999-0-54", "999000000000000054")]
    public void Bank_account_number_is_normalised_to_18_digits(string input, string expected)
    {
        var account = SerbianBankAccountNumber.Parse(input);

        Assert.Equal(expected, account.Value);
        Assert.Equal(expected[..3], account.BankCode);
        Assert.Equal($"{expected[..3]}-{expected[3..16]}-{expected[16..]}", account.ToDashedString());
    }

    [Fact]
    public void Bank_account_number_is_masked_by_default() =>
        Assert.Equal("205-*********6789-**", SerbianBankAccountNumber.Parse("205-123456789-68").ToString());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("160000000000510006")]
    [InlineData("160-5100-06")]
    [InlineData("16000000000051000")]
    [InlineData("160-0000000005100-055")]
    [InlineData("160-0000000005100-5")]
    [InlineData("16-5100-05")]
    [InlineData("160--05")]
    [InlineData("160-00000000005100-05")]
    [InlineData("160-51a0-05")]
    [InlineData("160-5100")]
    [InlineData("160-5100-05-1")]
    [InlineData("16000000000051000a")]
    [InlineData("160 5100 05")]
    [InlineData("1600000000005100050")]
    public void Invalid_bank_account_number_is_rejected(string? input)
    {
        Assert.False(SerbianBankAccountNumber.TryParse(input, out var account));
        Assert.Null(account);
        Assert.Throws<FormatException>(() => SerbianBankAccountNumber.Parse(input));
    }

    [Fact]
    public void Over_long_bank_account_input_is_rejected_before_parsing() =>
        Assert.False(SerbianBankAccountNumber.TryParse(new string('1', 10_000), out _));
}
