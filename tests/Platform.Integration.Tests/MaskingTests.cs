using Shared.Infrastructure;

namespace Platform.Integration.Tests;

public sealed class MaskingTests
{
    [Theory]
    [InlineData("ana.petrovic@firma.rs", "a***@firma.rs")]
    [InlineData("x@y.rs", "x***@y.rs")]
    [InlineData("no-at-sign", "***")]
    [InlineData("@firma.rs", "***")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Email_keeps_only_the_first_letter_and_the_domain(string? email, string expected) =>
        Assert.Equal(expected, Masking.Email(email));
}
