using HealthPilot.Api.Dtos;
using System.Text.Json;

namespace HealthPilot.Api.Ingestion.Parsers;

public class CmsJsonPricingParser : IPricingParser
{
    public async Task<IReadOnlyList<StructuredPricingRecord>> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        JsonElement root = await PricingLoader.LoadJsonAsync(filePath, cancellationToken);

        var output = new List<StructuredPricingRecord>();

        if (root.ValueKind != JsonValueKind.Array)
        {
            return output;
        }

        foreach (JsonElement row in root.EnumerateArray())
        {
            string cpt = Normalizers.NormalizeCptCode(row.GetPropertyOrDefault("cpt_code"));
            if (!Normalizers.IsValidCptOrHcpcs(cpt))
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
                LastUpdated = DateTimeOffset.UtcNow
            });
        }

        return output;
    }
}
