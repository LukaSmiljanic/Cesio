namespace Shared.Kernel;

/// <summary>Business days are Monday to Friday; the platform has no public holiday calendar yet.</summary>
public static class BusinessDays
{
    public static DateOnly Add(DateOnly from, int days)
    {
        var date = from;
        while (days > 0)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days--;
            }
        }

        return date;
    }
}
