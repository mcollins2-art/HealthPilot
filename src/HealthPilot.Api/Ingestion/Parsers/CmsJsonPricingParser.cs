using HealthPilot.Api.Dtos;
using System.Text.Json;
using System.Globalization;

namespace HealthPilot.Api.Ingestion.Parsers;

public class CmsJsonPricingParser : IPricingParser
{
    public async Task<IReadOnlyList<StructuredPricingRecord>> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        var sourceLastUpdated = File.GetLastWriteTimeUtc(filePath);
        JsonElement root = await PricingLoader.LoadJsonAsync(filePath, cancellationToken);

        var output = new List<StructuredPricingRecord>();

        if (root.ValueKind != JsonValueKind.Array)
        {
            return output;
        }

        foreach (JsonElement row in root.EnumerateArray())
        {
            string cpt = Normalizers.NormalizeCptCode(row.GetPropertyOrDefault("cpt_code"));
            if (string.IsNullOrWhiteSpace(cpt))
            {
                continue;
            }

            output.Add(new StructuredPricingRecord
            {
                CptCode = cpt,
                ProcedureDescription = row.GetPropertyOrDefault("description", "Unknown Procedure"),
                ProcedureCategory = row.GetPropertyOrDefault("category", "imaging"),
                FacilityName = row.GetPropertyOrDefault("facility_name", "Unknown Facility"),
                FacilityType = row.GetPropertyOrDefault("facility_type", "hospital"),
                City = row.GetPropertyOrDefault("city"),
                State = row.GetPropertyOrDefault("state"),
                ZipCode = row.GetPropertyOrDefault("zip"),
                InsurerName = Normalizers.NormalizeInsurerName(row.GetPropertyOrDefault("insurer")),
                NegotiatedRate = row.GetDecimalOrNull("negotiated_rate"),
                NegotiatedRateType = row.GetPropertyOrDefault("rate_type"),
                CashPrice = row.GetDecimalOrNull("cash_price"),
                LastUpdated = ParseDateTimeOffset(row.GetPropertyOrDefault("last_updated")) ?? sourceLastUpdated
            });
        }

        return output;
    }

    private static DateTimeOffset? ParseDateTimeOffset(string value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
