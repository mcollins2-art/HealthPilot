using CsvHelper;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.Json;

namespace HealthPilot.Api.Ingestion;

public static class PricingLoader
{
    // Generic JSON loader for CMS machine-readable files.
    public static async Task<JsonElement> LoadJsonAsync(string filePath, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(filePath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    // Generic CSV loader producing key-value rows for downstream normalization.
    public static List<Dictionary<string, string>> LoadCsv(string filePath)
    {
        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        return csv.GetRecords<dynamic>()
            .Select(row => (IDictionary<string, object>)row)
            .Select(dict => dict.ToDictionary(k => k.Key, v => v.Value?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    // Streaming CSV loader used for large-file batch ingestion workflows.
    public static IEnumerable<Dictionary<string, string>> StreamCsvRows(string filePath)
    {
        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        if (!csv.Read())
        {
            yield break;
        }

        csv.ReadHeader();

        var headers = csv.HeaderRecord!;

        while (csv.Read())
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
            {
                row[header] = csv.GetField(header)!;
            }

            yield return row;
        }
    }

    // Streaming JSON loader for array-based machine-readable files.
    public static async IAsyncEnumerable<Dictionary<string, string>> StreamJsonRowsAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        if (!await HasArrayRootAsync(stream, cancellationToken))
        {
            yield break;
        }

        stream.Position = 0;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        await foreach (var row in JsonSerializer.DeserializeAsyncEnumerable<Dictionary<string, JsonElement>>(
                           stream,
                           options,
                           cancellationToken))
        {
            if (row is null)
            {
                continue;
            }

            var mapped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in row)
            {
                mapped[kvp.Key] = kvp.Value.ToString();
            }

            yield return mapped;
        }
    }

    private static async Task<bool> HasArrayRootAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (!stream.CanSeek)
        {
            return false;
        }

        stream.Position = 0;
        var bom = new byte[3];
        var bomRead = await stream.ReadAsync(bom.AsMemory(0, bom.Length), cancellationToken);
        if (bomRead != 3 || bom[0] != 0xEF || bom[1] != 0xBB || bom[2] != 0xBF)
        {
            stream.Position = 0;
        }

        var buffer = new byte[1024];

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                return false;
            }

            for (var i = 0; i < read; i++)
            {
                var current = buffer[i];
                if (!IsJsonWhitespace(current))
                {
                    return current == (byte)'[';
                }
            }
        }
    }

    private static bool IsJsonWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r';
    }
}
