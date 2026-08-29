using System.Text;

namespace QrSimple.Api;

// Builds a Content-Disposition: inline header carrying a generated, self-describing filename
// (see docs/plans/0002-inspection-records.md decision 19). Do not pass fileDownloadName to
// Results.File for this — it forces "attachment", which stops phones displaying the PDF
// inline. Equipment names may contain Cyrillic, so this pairs an ASCII-sanitised filename
// fallback with an RFC 5987 filename* for clients that support it (every modern browser).
public static class ContentDisposition
{
    public static string BuildInlineHeader(string equipmentName, string kind, DateOnly inspectionDate)
    {
        var rawName = $"{equipmentName}-{kind}-{inspectionDate:yyyy-MM-dd}.pdf";
        var sanitized = SanitizeForPath(rawName);

        var asciiFallback = ToAsciiFallback(sanitized);
        var rfc5987Encoded = Uri.EscapeDataString(sanitized);

        return $"""inline; filename="{asciiFallback}"; filename*=UTF-8''{rfc5987Encoded}""";
    }

    private static string SanitizeForPath(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\']).ToHashSet();
        return new string(value.Select(c => invalid.Contains(c) ? '-' : c).ToArray());
    }

    // A quoted filename in Content-Disposition must stay within the header's field-content
    // grammar (effectively ASCII) — non-ASCII characters (e.g. Cyrillic equipment names) are
    // dropped here and carried instead by filename*, which every modern browser prefers when
    // both are present.
    private static string ToAsciiFallback(string value)
    {
        var builder = new StringBuilder();
        foreach (var c in value)
        {
            if (c < 128)
            {
                builder.Append(c == '"' ? '\'' : c);
            }
        }

        var ascii = builder.ToString().Trim(' ', '-');
        return string.IsNullOrWhiteSpace(ascii) ? "inspection.pdf" : ascii;
    }
}
