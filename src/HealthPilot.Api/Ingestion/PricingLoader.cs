using CsvHelper;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.Json;

namespace HealthPilot.Api.Ingestion;

/// <summary>
/// Provides low-level file loading utilities for CMS machine-readable pricing files.
/// Includes both in-memory loaders (for small files or parsers that need full-document access)
/// and streaming enumerators (for large-file batch ingestion workflows).
/// </summary>
public static class PricingLoader
{
    /// <summary>
    /// Loads and parses the entire JSON file at <paramref name="filePath"/> into a <see cref="JsonElement"/>.
    /// Suitable for CMS JSON machine-readable files that must be traversed as a full document.
    /// </summary>
    /// <param name="filePath">Absolute path to the JSON file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A cloned <see cref="JsonElement"/> representing the document root.</returns>
    public static async Task<JsonElement> LoadJsonAsync(string filePath, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(filePath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Loads all rows from the CSV file at <paramref name="filePath"/> into memory as key-value dictionaries.
    /// Column keys are case-insensitive. Suitable for small files or non-streaming parsers.
    /// </summary>
    /// <param name="filePath">Absolute path to the CSV file.</param>
    /// <returns>A list of rows, each represented as a case-insensitive string dictionary.</returns>
    public static List<Dictionary<string, string>> LoadCsv(string filePath)
    {
        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        return csv.GetRecords<dynamic>()
            .Select(row => (IDictionary<string, object>)row)
            .Select(dict => dict.ToDictionary(k => k.Key, v => v.Value?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Streams rows from the CSV file at <paramref name="filePath"/> one at a time, enabling
    /// memory-efficient processing of large files in the batched ingestion pipeline.
    /// Returns an empty sequence if the file has no rows.
    /// </summary>
    /// <param name="filePath">Absolute path to the CSV file.</param>
    /// <returns>An enumerable of rows, each represented as a case-insensitive string dictionary.</returns>
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

    /// <summary>
    /// Asynchronously streams rows from a JSON array-of-objects pricing file, enabling
    /// memory-efficient processing of large CMS machine-readable JSON files.
    /// Rows that fail to deserialize are silently skipped.
    /// </summary>
    /// <param name="filePath">Absolute path to the JSON file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of rows, each represented as a case-insensitive string dictionary.</returns>
    public static async IAsyncEnumerable<Dictionary<string, string>> StreamJsonRowsAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
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
}
