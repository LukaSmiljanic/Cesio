using System.Globalization;
using System.Text;

namespace Modules.Organizations;

/// <summary>Renders the pristupnica document from the organization's data (task 007a).</summary>
internal interface IPristupnicaRenderer
{
    byte[] Render(IReadOnlyList<string> lines);
}

/// <summary>
/// A minimal PDF writer (text lines in Helvetica, A4, as many pages as needed), so the MVP needs no PDF library.
/// Serbian letters outside WinAnsi are written without their diacritic (č → c, đ → dj); the stored data keeps them.
/// </summary>
internal sealed class SimplePdfRenderer : IPristupnicaRenderer
{
    private const int LinesPerPage = 48;

    public byte[] Render(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var pages = lines.Chunk(LinesPerPage).ToList();
        if (pages.Count == 0)
        {
            pages.Add([]);
        }

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', pages.Select((_, i) => $"{4 + (i * 2)} 0 R"))}] /Count {pages.Count} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };
        foreach (var page in pages)
        {
            var content = new StringBuilder("BT /F1 10 Tf 50 800 Td 14 TL\n");
            foreach (var line in page)
            {
                content.Append(CultureInfo.InvariantCulture, $"({Escape(Ascii(line))}) Tj T*\n");
            }

            content.Append("ET");
            var contentObject = 4 + objects.Count - 3 + 1;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentObject} 0 R >>");
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content.ToString())} >>\nstream\n{content}\nendstream");
        }

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);

    private static string Ascii(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text.Replace("đ", "dj", StringComparison.Ordinal).Replace("Đ", "Dj", StringComparison.Ordinal).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c <= 0x7E && c >= 0x20 ? c : '?');
            }
        }

        return builder.ToString();
    }
}
