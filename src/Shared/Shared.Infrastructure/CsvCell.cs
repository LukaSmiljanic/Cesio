namespace Shared.Infrastructure;

/// <summary>CSV export cells (task 022; the fee export of task 018 has its own copy).</summary>
public static class CsvCell
{
    /// <summary>
    /// A text cell: quoted when it holds a separator, quote or line break; a leading <c>= + - @</c>, tab or CR is
    /// neutralised with an apostrophe so a spreadsheet never runs it as a formula.
    /// </summary>
    public static string Text(string? value)
    {
        value ??= string.Empty;
        var safe = value.Length > 0 && "=+-@\t\r".Contains(value[0], StringComparison.Ordinal) ? "'" + value : value;
        return safe.Contains(';', StringComparison.Ordinal) || safe.Contains('"', StringComparison.Ordinal) || safe.Contains('\n', StringComparison.Ordinal)
            ? $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : safe;
    }
}
