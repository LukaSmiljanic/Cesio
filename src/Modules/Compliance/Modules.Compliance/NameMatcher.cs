using System.Globalization;
using System.Text;

namespace Modules.Compliance;

/// <summary>
/// Fuzzy name matching for screening (task 007): names are normalised (Serbian Cyrillic transliterated to Latin,
/// diacritics removed, case and punctuation ignored, words sorted) and compared with Jaro-Winkler.
/// </summary>
internal static class NameMatcher
{
    private static readonly Dictionary<char, string> Cyrillic = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['ђ'] = "dj", ['е'] = "e", ['ж'] = "z", ['з'] = "z", ['и'] = "i",
        ['ј'] = "j", ['к'] = "k", ['л'] = "l", ['љ'] = "lj", ['м'] = "m", ['н'] = "n", ['њ'] = "nj", ['о'] = "o", ['п'] = "p", ['р'] = "r",
        ['с'] = "s", ['т'] = "t", ['ћ'] = "c", ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "c", ['ч'] = "c", ['џ'] = "dz", ['ш'] = "s",
    };

    /// <summary>"Петровић Марко" and "marko PETROVIĆ" both become "marko petrovic".</summary>
    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var latin = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            latin.Append(Cyrillic.TryGetValue(c, out var replacement) ? replacement : c.ToString());
        }

        var stripped = new StringBuilder();
        foreach (var c in latin.ToString().Replace("đ", "dj", StringComparison.Ordinal).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            // "d.o.o." and "doo", "O'Brien" and "OBrien" are the same word.
            if (c is '.' or '\'' or '’')
            {
                continue;
            }

            stripped.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return string.Join(' ', stripped.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
    }

    /// <summary>Jaro-Winkler similarity of two normalised names, 0 to 1.</summary>
    public static decimal Similarity(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Length == 0 || b.Length == 0)
        {
            return 0m;
        }

        if (a == b)
        {
            return 1m;
        }

        var window = Math.Max(0, (Math.Max(a.Length, b.Length) / 2) - 1);
        var aMatched = new bool[a.Length];
        var bMatched = new bool[b.Length];
        var matches = 0;
        for (var i = 0; i < a.Length; i++)
        {
            for (var j = Math.Max(0, i - window); j < Math.Min(b.Length, i + window + 1); j++)
            {
                if (!bMatched[j] && a[i] == b[j])
                {
                    aMatched[i] = bMatched[j] = true;
                    matches++;
                    break;
                }
            }
        }

        if (matches == 0)
        {
            return 0m;
        }

        var transpositions = 0;
        for (int i = 0, k = 0; i < a.Length; i++)
        {
            if (!aMatched[i])
            {
                continue;
            }

            while (!bMatched[k])
            {
                k++;
            }

            if (a[i] != b[k])
            {
                transpositions++;
            }

            k++;
        }

        decimal m = matches;
        var jaro = ((m / a.Length) + (m / b.Length) + ((m - (transpositions / 2m)) / m)) / 3m;
        var prefix = 0;
        while (prefix < Math.Min(4, Math.Min(a.Length, b.Length)) && a[prefix] == b[prefix])
        {
            prefix++;
        }

        return decimal.Round(jaro + (prefix * 0.1m * (1m - jaro)), 4);
    }
}
