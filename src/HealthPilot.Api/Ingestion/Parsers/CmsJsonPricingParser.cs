using HealthPilot.Api.Dtos;
using System.Text.Json;

namespace HealthPilot.Api.Ingestion.Parsers;

/// <summary>
/// Parses CMS machine-readable pricing files in JSON array format (each element representing
/// one negotiated rate or cash price entry) into <see cref="StructuredPricingRecord"/> instances.
/// </summary>
/// <remarks>
/// The JSON file must be an array at the root level. Expected JSON properties (case-insensitive):
/// <c>cpt_code</c>, <c>description</c>, <c>category</c>, <c>facility_name</c>, <c>facility_type</c>,
/// <c>city</c>, <c>state</c>, <c>zip</c>, <c>insurer</c>, <c>negotiated_rate</c>, <c>rate_type</c>,
/// <c>cash_price</c>. Elements missing a valid CPT code are silently skipped.
/// </remarks>
public class CmsJsonPricingParser : IPricingParser
{
    /// <inheritdoc/>
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
                LastUpdated = DateTimeOffset.UtcNow
            });
        }

        return output;
    }
}
