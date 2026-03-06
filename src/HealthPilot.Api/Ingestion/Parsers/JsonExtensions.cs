using System.Text.Json;

namespace HealthPilot.Api.Ingestion.Parsers;

/// <summary>
/// Extension methods for working with <see cref="JsonElement"/> in a null-safe, fallback-friendly way.
/// </summary>
internal static class JsonExtensions
{
    /// <summary>
    /// Gets the string value of the property named <paramref name="name"/> from <paramref name="element"/>,
    /// or <paramref name="fallback"/> if the property is absent.
    /// </summary>
    public static string GetPropertyOrDefault(this JsonElement element, string name, string fallback = "")
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        return value.ToString();
    }

    /// <summary>
    /// Gets the decimal value of the property named <paramref name="name"/> from <paramref name="element"/>,
    /// or <c>null</c> if the property is absent or cannot be parsed as a decimal.
    /// </summary>
    public static decimal? GetDecimalOrNull(this JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal n))
        {
            return n;
        }

        return decimal.TryParse(value.ToString(), out decimal parsed) ? parsed : null;
    }
}
