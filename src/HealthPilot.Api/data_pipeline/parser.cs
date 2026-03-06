using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HealthPilot.Api.DataPipeline;

public sealed record TransparencyRecord(
    string? HospitalName,
    string? Payer,
    string? ProcedureCode,
    string? ProcedureDescription,
    decimal? NegotiatedRate,
    decimal? CashPrice,
    string? Location);

public class StreamingParser
{
    private static readonly Dictionary<string, string[]> FieldAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hospital_name"] = ["hospital_name", "provider_name", "facility_name", "hospital"],
        ["payer"] = ["payer", "insurer", "plan", "insurance_plan"],
        ["procedure_code"] = ["procedure_code", "cpt", "cpt_code", "code"],
        ["procedure_description"] = ["procedure_description", "description", "service_description"],
        ["negotiated_rate"] = ["negotiated_rate", "rate", "negotiated_amount"],
        ["cash_price"] = ["cash_price", "self_pay_price", "discounted_cash_price"],
        ["location"] = ["location", "address", "city_state", "site", "city"]
    };

    public IAsyncEnumerable<TransparencyRecord> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".csv" => ParseCsvAsync(filePath, cancellationToken),
            ".json" => ParseJsonAsync(filePath, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported transparency file type: {extension}")
        };
    }

    private async IAsyncEnumerable<TransparencyRecord> ParseCsvAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            MissingFieldFound = null,
            BadDataFound = null,
            IgnoreBlankLines = true
        };

        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync())
        {
            yield break;
        }

        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? [];

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
            {
                row[header] = csv.TryGetField(header, out string? value) ? value ?? string.Empty : string.Empty;
            }

            yield return MapRecord(row);
        }
    }

    private async IAsyncEnumerable<TransparencyRecord> ParseJsonAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<Dictionary<string, JsonElement>>(stream, cancellationToken: cancellationToken))
        {
            if (item is null)
            {
                continue;
            }

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in item)
            {
                row[kvp.Key] = kvp.Value.ToString();
            }

            yield return MapRecord(row);
        }
    }

    private static TransparencyRecord MapRecord(Dictionary<string, string> row)
    {
        string? GetValue(string key)
        {
            foreach (var alias in FieldAliases[key])
            {
                if (row.TryGetValue(alias, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        return new TransparencyRecord(
            HospitalName: GetValue("hospital_name"),
            Payer: GetValue("payer"),
            ProcedureCode: GetValue("procedure_code"),
            ProcedureDescription: GetValue("procedure_description"),
            NegotiatedRate: ParseDecimal(GetValue("negotiated_rate")),
            CashPrice: ParseDecimal(GetValue("cash_price")),
            Location: GetValue("location"));
    }

    private static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return decimal.TryParse(raw, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
