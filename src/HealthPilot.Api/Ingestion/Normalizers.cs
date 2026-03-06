using System.Text.RegularExpressions;

namespace HealthPilot.Api.Ingestion;

/// <summary>
/// Provides string normalization utilities for CPT codes and insurer names, ensuring
/// consistent representation across all ingestion paths.
/// </summary>
public static class Normalizers
{
    /// <summary>
    /// Strips all non-alphanumeric characters from <paramref name="raw"/> and converts the result to uppercase.
    /// Returns an empty string if <paramref name="raw"/> is null or empty.
    /// </summary>
    /// <param name="raw">Raw CPT code string from a parsed pricing file.</param>
    /// <returns>Normalized CPT code (uppercase alphanumeric only), or an empty string.</returns>
    public static string NormalizeCptCode(string raw)
    {
        string cleaned = Regex.Replace(raw ?? string.Empty, "[^A-Za-z0-9]", string.Empty);
        return cleaned.ToUpperInvariant();
    }

    /// <summary>
    /// Trims whitespace, collapses internal whitespace runs to a single space, and converts to uppercase.
    /// Returns an empty string if <paramref name="raw"/> is null or whitespace-only.
    /// </summary>
    /// <param name="raw">Raw insurer name string from a parsed pricing file.</param>
    /// <returns>Normalized insurer name (uppercase, single-space separated), or an empty string.</returns>
    public static string NormalizeInsurerName(string raw)
    {
        var collapsed = Regex.Replace((raw ?? string.Empty).Trim(), "\\s+", " ");
        return collapsed.ToUpperInvariant();
    }
}
