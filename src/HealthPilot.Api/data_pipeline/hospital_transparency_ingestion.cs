using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace HealthPilot.Api.DataPipeline;

public sealed record DownloadedTransparencyFile(Uri Source, string LocalPath);

public class HospitalTransparencyIngestionWorkflow(
    TransparencyMachineReadableLinkDiscoverer linkDiscoverer,
    Downloader downloader,
    StreamingParser parser,
    ILogger<HospitalTransparencyIngestionWorkflow> logger)
{
    private static readonly Regex NonAlphanumeric = new("[^A-Za-z0-9]", RegexOptions.Compiled);
    private static readonly Regex NumericCptPattern = new("^\\d{1,5}$", RegexOptions.Compiled);
    private static readonly Regex AlphaNumericCptPattern = new("^[A-Z]\\d{4}$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<DownloadedTransparencyFile>> DownloadMachineReadableFilesAsync(
        IEnumerable<Uri> transparencyPages,
        string? dataDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var outputDirectory = ResolveDataDirectory(dataDirectory);
        Directory.CreateDirectory(outputDirectory);

        var downloaded = new List<DownloadedTransparencyFile>();
        foreach (var page in transparencyPages.Distinct())
        {
            var links = await linkDiscoverer.DiscoverAsync(page, cancellationToken);
            foreach (var link in links)
            {
                var destinationPath = BuildDestinationPath(outputDirectory, link, downloaded);
                var localPath = await downloader.DownloadAsync(link, destinationPath, cancellationToken: cancellationToken);
                downloaded.Add(new DownloadedTransparencyFile(link, localPath));
            }
        }

        return downloaded;
    }

    public async Task<IReadOnlyList<TransparencyRecord>> ParseCptNegotiatedAndCashRatesAsync(
        IEnumerable<string> downloadedFiles,
        CancellationToken cancellationToken = default)
    {
        var extracted = new List<TransparencyRecord>();
        foreach (var file in downloadedFiles)
        {
            await foreach (var record in parser.ParseAsync(file, cancellationToken))
            {
                if (!LooksLikeCptCode(record.ProcedureCode))
                {
                    continue;
                }

                var normalizedCpt = Normalizer.NormalizeCptCode(record.ProcedureCode);
                if (string.IsNullOrWhiteSpace(normalizedCpt))
                {
                    continue;
                }

                if (record.NegotiatedRate is null && record.CashPrice is null)
                {
                    continue;
                }

                extracted.Add(record with { ProcedureCode = normalizedCpt });
            }
        }

        logger.LogInformation("Extracted {Count} CPT-coded records with negotiated/cash prices", extracted.Count);
        return extracted;
    }

    public async Task<IReadOnlyList<TransparencyRecord>> DownloadAndParseCptRatesAsync(
        IEnumerable<Uri> transparencyPages,
        string? dataDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var downloaded = await DownloadMachineReadableFilesAsync(transparencyPages, dataDirectory, cancellationToken);
        return await ParseCptNegotiatedAndCashRatesAsync(downloaded.Select(file => file.LocalPath), cancellationToken);
    }

    private static string ResolveDataDirectory(string? dataDirectory)
    {
        if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            return dataDirectory;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "data");
    }

    private static string BuildDestinationPath(
        string outputDirectory,
        Uri source,
        IEnumerable<DownloadedTransparencyFile> previouslyDownloaded)
    {
        var fileName = Path.GetFileName(source.AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            var extension = Path.GetExtension(source.AbsolutePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".json";
            }

            fileName = $"{source.Host}{extension}";
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extensionName = Path.GetExtension(fileName);

        var existing = new HashSet<string>(
            previouslyDownloaded.Select(file => Path.GetFileName(file.LocalPath)),
            StringComparer.OrdinalIgnoreCase);

        var candidateName = fileName;
        var counter = 2;
        while (existing.Contains(candidateName) || File.Exists(Path.Combine(outputDirectory, candidateName)))
        {
            candidateName = $"{baseName}_{counter}{extensionName}";
            counter++;
        }

        return Path.Combine(outputDirectory, candidateName);
    }

    private static bool LooksLikeCptCode(string? rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return false;
        }

        var cleaned = NonAlphanumeric.Replace(rawCode.Trim().ToUpperInvariant(), string.Empty);
        return NumericCptPattern.IsMatch(cleaned) || AlphaNumericCptPattern.IsMatch(cleaned);
    }
}
