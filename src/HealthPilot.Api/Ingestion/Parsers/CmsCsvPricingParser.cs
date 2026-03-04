using HealthPilot.Api.Dtos;
using System.Globalization;

namespace HealthPilot.Api.Ingestion.Parsers;

public class CmsCsvPricingParser : IPricingParser
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".csv"];

    public Task<IReadOnlyList<StructuredPricingRecord>> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        var sourceLastUpdated = File.GetLastWriteTimeUtc(filePath);
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
                LastUpdated = ParseDateTimeOffset(GetValue(row, "last_updated")) ?? sourceLastUpdated
            });
        }

        return Task.FromResult<IReadOnlyList<StructuredPricingRecord>>(output);
    }

    private static string GetValue(Dictionary<string, string> row, string key, string fallback = "")
    {
        return row.TryGetValue(key, out var value) ? value : fallback;
    }

    private static decimal? ParseDecimal(string value) => Normalizers.ParseDecimalInvariantOrNull(value);

    private static DateTimeOffset? ParseDateTimeOffset(string value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }
}
