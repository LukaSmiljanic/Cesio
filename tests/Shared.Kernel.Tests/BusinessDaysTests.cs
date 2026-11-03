using System.Globalization;

namespace Shared.Kernel.Tests;

/// <summary><see cref="BusinessDays"/> skips weekends only (no holiday calendar).</summary>
public sealed class BusinessDaysTests
{
    [Theory]
    [InlineData("2026-10-21", 0, "2026-10-21")] // Wednesday, nothing added
    [InlineData("2026-10-21", 1, "2026-10-22")]
    [InlineData("2026-10-23", 1, "2026-10-26")] // Friday → Monday
    [InlineData("2026-10-24", 1, "2026-10-26")] // from a Saturday
    [InlineData("2026-10-25", 1, "2026-10-26")] // from a Sunday
    [InlineData("2026-10-21", 5, "2026-10-28")]
    public void Adding_business_days_skips_weekends(string start, int days, string expected) =>
        Assert.Equal(
            DateOnly.Parse(expected, CultureInfo.InvariantCulture),
            BusinessDays.Add(DateOnly.Parse(start, CultureInfo.InvariantCulture), days));
}
