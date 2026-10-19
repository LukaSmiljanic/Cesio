using System.Globalization;

namespace Shared.Kernel.Tests;

/// <summary>The non-working days below are test data, not the official holiday table.</summary>
public sealed class BusinessDayCalendarTests
{
    private static readonly BusinessDayCalendar Calendar = new(
    [
        new DateOnly(2026, 12, 31),
        new DateOnly(2027, 1, 1),
        new DateOnly(2027, 1, 7),
    ]);

    [Theory]
    [InlineData("2026-12-28", true)] // Monday
    [InlineData("2026-12-31", false)] // non-working Thursday
    [InlineData("2027-01-02", false)] // Saturday
    [InlineData("2027-01-03", false)] // Sunday
    [InlineData("2027-01-04", true)] // Monday
    public void Weekends_and_listed_days_are_not_business_days(string date, bool expected) =>
        Assert.Equal(expected, Calendar.IsBusinessDay(DateOnly.Parse(date, CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("2026-12-29", 0, "2026-12-29")]
    [InlineData("2026-12-29", 1, "2026-12-30")]
    [InlineData("2026-12-29", 2, "2027-01-04")] // skips 31 Dec, 1 Jan and the weekend
    [InlineData("2027-01-05", 1, "2027-01-06")]
    [InlineData("2027-01-06", 1, "2027-01-08")] // skips 7 Jan
    [InlineData("2027-01-08", 5, "2027-01-15")]
    [InlineData("2027-01-02", 1, "2027-01-04")] // from a Saturday
    public void Adding_business_days_skips_weekends_and_listed_days(string start, int days, string expected) =>
        Assert.Equal(
            DateOnly.Parse(expected, CultureInfo.InvariantCulture),
            Calendar.AddBusinessDays(DateOnly.Parse(start, CultureInfo.InvariantCulture), days));

    [Fact]
    public void Negative_business_days_are_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.AddBusinessDays(new DateOnly(2027, 1, 4), -1));

    [Fact]
    public void Calendar_requires_its_data() =>
        Assert.Throws<ArgumentNullException>(() => new BusinessDayCalendar(null!));

    [Theory]
    [InlineData("2026-12-31T22:59:59Z", "2026-12-31")] // 23:59:59 in Belgrade (CET, UTC+1)
    [InlineData("2026-12-31T23:00:00Z", "2027-01-01")] // midnight in Belgrade
    [InlineData("2026-07-14T21:59:59Z", "2026-07-14")] // 23:59:59 in Belgrade (CEST, UTC+2)
    [InlineData("2026-07-14T22:00:00Z", "2026-07-15")]
    public void Today_is_the_date_in_Belgrade(string utcNow, string expected) =>
        Assert.Equal(
            DateOnly.Parse(expected, CultureInfo.InvariantCulture),
            BusinessDayCalendar.Today(new FixedTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture))));

    [Fact]
    public void Today_requires_a_time_provider() =>
        Assert.Throws<ArgumentNullException>(() => BusinessDayCalendar.Today(null!));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
