using System.Globalization;
using System.Text.RegularExpressions;

namespace HealthPilot.Api.Ingestion;

public static class Normalizers
{
    public static string NormalizeCptCode(string raw)
    {
        string cleaned = Regex.Replace(raw ?? string.Empty, "[^A-Za-z0-9]", string.Empty);
        return cleaned.ToUpperInvariant();
    }

    public static string NormalizeInsurerName(string raw)
    {
        var collapsed = Regex.Replace((raw ?? string.Empty).Trim(), "\\s+", " ");
        return collapsed.ToUpperInvariant();
    }

    public static decimal? ParseDecimalInvariantOrNull(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
