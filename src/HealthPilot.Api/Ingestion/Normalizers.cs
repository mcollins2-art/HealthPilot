using System.Text.RegularExpressions;

namespace HealthPilot.Api.Ingestion;

public static class Normalizers
{
    private static readonly Regex CptRegex = new("^[A-Z0-9]{5}$", RegexOptions.Compiled);
    private static readonly Regex HcpcsRegex = new("^[A-Z0-9]{4}-[A-Z0-9]{2}$", RegexOptions.Compiled);

    public static string NormalizeCptCode(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var upper = raw.Trim().ToUpperInvariant();
        var collapsedWhitespace = Regex.Replace(upper, "\\s+", string.Empty);
        if (HcpcsRegex.IsMatch(collapsedWhitespace))
        {
            return collapsedWhitespace;
        }

        return Regex.Replace(collapsedWhitespace, "[^A-Z0-9]", string.Empty);
    }

    public static bool IsValidCptOrHcpcs(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return CptRegex.IsMatch(value) || HcpcsRegex.IsMatch(value);
    }

    public static string NormalizeInsurerName(string raw)
    {
        var collapsed = Regex.Replace((raw ?? string.Empty).Trim(), "\\s+", " ");
        return collapsed.ToUpperInvariant();
    }
}
