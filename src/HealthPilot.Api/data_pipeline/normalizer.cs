using System.Text.RegularExpressions;

namespace HealthPilot.Api.DataPipeline;

public sealed record NormalizedProcedure(string CptCode, string Description, string Category);

public static class Normalizer
{
    private static readonly Regex NonAlphanumeric = new("[^A-Za-z0-9]", RegexOptions.Compiled);
    private static readonly Regex DigitsOnly = new("\\D", RegexOptions.Compiled);
    private static readonly Regex AlphaNumericCptPattern = new("^[A-Z]\\d{4}$", RegexOptions.Compiled);
    private static readonly Regex NumericCptPattern = new("^\\d{5}$", RegexOptions.Compiled);

    public static string? NormalizeCptCode(string? rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return null;
        }

        var cleaned = NonAlphanumeric.Replace(rawCode.Trim().ToUpperInvariant(), string.Empty);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        if (AlphaNumericCptPattern.IsMatch(cleaned) || NumericCptPattern.IsMatch(cleaned))
        {
            return cleaned;
        }

        var digits = DigitsOnly.Replace(cleaned, string.Empty);
        if (digits.Length >= 5)
        {
            return digits[..5];
        }

        if (digits.Length > 0)
        {
            return digits.PadLeft(5, '0');
        }

        return null;
    }

    public static string CategorizeProcedure(string? cptCode)
    {
        if (string.IsNullOrWhiteSpace(cptCode))
        {
            return "Unknown";
        }

        var digits = DigitsOnly.Replace(cptCode, string.Empty);
        if (!int.TryParse(digits, out var value))
        {
            return "Unknown";
        }

        return value switch
        {
            >= 70000 and <= 79999 => "Radiology",
            >= 80000 and <= 89999 => "Laboratory",
            >= 90000 and <= 99999 => "Medicine",
            >= 10000 and <= 69999 => "Surgery and Procedures",
            _ => "Other"
        };
    }

    public static NormalizedProcedure? NormalizeProcedure(TransparencyRecord record)
    {
        var cpt = NormalizeCptCode(record.ProcedureCode);
        if (string.IsNullOrWhiteSpace(cpt))
        {
            return null;
        }

        var description = string.IsNullOrWhiteSpace(record.ProcedureDescription)
            ? "Unknown Procedure"
            : record.ProcedureDescription.Trim();

        return new NormalizedProcedure(cpt, description, CategorizeProcedure(cpt));
    }
}
