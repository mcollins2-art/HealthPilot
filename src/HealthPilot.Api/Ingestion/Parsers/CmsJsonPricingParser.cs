using HealthPilot.Api.Dtos;
namespace HealthPilot.Api.Ingestion.Parsers;

public class CmsJsonPricingParser : IPricingParser
{
    public async Task<IReadOnlyList<StructuredPricingRecord>> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        var output = new List<StructuredPricingRecord>();

        await foreach (var row in PricingLoader.StreamJsonRowsAsync(filePath, cancellationToken))
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

        return output;
    }

    private static string GetValue(Dictionary<string, string> row, string key, string fallback = "")
    {
        return row.TryGetValue(key, out var value) && value is not null ? value : fallback;
    }

    private static decimal? ParseDecimal(string value)
    {
        return decimal.TryParse(value, out var parsed) ? parsed : null;
    }
}
