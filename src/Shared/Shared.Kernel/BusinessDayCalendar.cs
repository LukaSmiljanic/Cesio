namespace Shared.Kernel;

/// <summary>
/// Business days in Serbia: Monday–Friday except the non-working days it is given. The non-working days are data
/// (a configurable table, domain-model.md §4); they are never computed here, Orthodox Easter included.
/// </summary>
public sealed class BusinessDayCalendar
{
    private static readonly TimeZoneInfo Belgrade = TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade");

    private readonly HashSet<DateOnly> _nonWorkingDays;

    public BusinessDayCalendar(IEnumerable<DateOnly> nonWorkingDays)
    {
        ArgumentNullException.ThrowIfNull(nonWorkingDays);
        _nonWorkingDays = [.. nonWorkingDays];
    }

    /// <summary>Today's date in Europe/Belgrade, which is the business date for "today" (architecture.md §5).</summary>
    public static DateOnly Today(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), Belgrade).DateTime);
    }

    /// <summary>The Belgrade business date of an instant.</summary>
    public static DateOnly DateOf(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Belgrade).DateTime);

    public bool IsBusinessDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !_nonWorkingDays.Contains(date);

    /// <summary>The date <paramref name="businessDays"/> business days after <paramref name="start"/>; 0 returns <paramref name="start"/>.</summary>
    public DateOnly AddBusinessDays(DateOnly start, int businessDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(businessDays);
        var date = start;
        for (var remaining = businessDays; remaining > 0;)
        {
            date = date.AddDays(1);
            if (IsBusinessDay(date))
            {
                remaining--;
            }
        }

        return date;
    }
}
