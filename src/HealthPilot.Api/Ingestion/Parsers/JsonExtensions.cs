using System.Text.Json;

namespace HealthPilot.Api.Ingestion.Parsers;

internal static class JsonExtensions
{
    public static string GetPropertyOrDefault(this JsonElement element, string name, string fallback = "")
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        return value.ToString();
    }

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
