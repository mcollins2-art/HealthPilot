using HealthPilot.Api.Dtos;

namespace HealthPilot.Api.Ingestion.Parsers;

/// <summary>
/// Parses CMS machine-readable pricing files in flat CSV format
/// (one row per negotiated rate or cash price) into <see cref="StructuredPricingRecord"/> instances.
/// </summary>
/// <remarks>
/// Expected CSV columns (case-insensitive): <c>cpt_code</c>, <c>description</c>, <c>category</c>,
/// <c>facility_name</c>, <c>facility_type</c>, <c>city</c>, <c>state</c>, <c>zip</c>,
/// <c>insurer</c>, <c>negotiated_rate</c>, <c>rate_type</c>, <c>cash_price</c>.
/// Rows missing a valid CPT code are silently skipped.
/// </remarks>
public class CmsCsvPricingParser : IPricingParser
{
    /// <inheritdoc/>
    public Task<IReadOnlyList<StructuredPricingRecord>> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        var rows = PricingLoader.LoadCsv(filePath);
        var output = new List<StructuredPricingRecord>();

        foreach (var row in rows)
        {
            string cpt = Normalizers.NormalizeCptCode(GetValue(row, "cpt_code"));
            if (string.IsNullOrWhiteSpace(cpt))
            {
                continue;
            }

            output.Add(new StructuredPricingRecord
            {
                CptCode = cpt,
                ProcedureDescription = GetValue(row, "description", "Unknown Procedure"),
                ProcedureCategory = GetValue(row, "category", "imaging"),
                FacilityName = GetValue(row, "facility_name", "Unknown Facility"),
                FacilityType = GetValue(row, "facility_type", "hospital"),
                City = GetValue(row, "city"),
                State = GetValue(row, "state"),
                ZipCode = GetValue(row, "zip"),
                InsurerName = Normalizers.NormalizeInsurerName(GetValue(row, "insurer")),
                NegotiatedRate = ParseDecimal(GetValue(row, "negotiated_rate")),
                NegotiatedRateType = GetValue(row, "rate_type"),
                CashPrice = ParseDecimal(GetValue(row, "cash_price")),
                LastUpdated = DateTimeOffset.UtcNow
            });
        }

        return Task.FromResult<IReadOnlyList<StructuredPricingRecord>>(output);
    }

    private static string GetValue(Dictionary<string, string> row, string key, string fallback = "")
    {
        return row.TryGetValue(key, out var value) ? value : fallback;
    }

    private static decimal? ParseDecimal(string value)
    {
        return decimal.TryParse(value, out var parsed) ? parsed : null;
    }
}
