using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.RegularExpressions;

namespace HealthPilot.Api.DataPipeline;

public class TransparencyMachineReadableLinkDiscoverer(HttpClient httpClient, ILogger<TransparencyMachineReadableLinkDiscoverer> logger)
{
    private static readonly Regex HrefRegex = new(
        "href\\s*=\\s*(?:\"(?<href_double_quoted>[^\"]+)\"|'(?<href_single_quoted>[^']+)'|(?<href_unquoted>[^\\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<IReadOnlyList<Uri>> DiscoverAsync(Uri transparencyPage, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(transparencyPage, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in HrefRegex.Matches(html))
        {
            var rawHref = match.Groups["href_double_quoted"].Value;
            if (string.IsNullOrWhiteSpace(rawHref))
            {
                rawHref = match.Groups["href_single_quoted"].Value;
            }

            if (string.IsNullOrWhiteSpace(rawHref))
            {
                rawHref = match.Groups["href_unquoted"].Value;
            }

            rawHref = WebUtility.HtmlDecode(rawHref).Trim();
            if (string.IsNullOrWhiteSpace(rawHref))
            {
                continue;
            }

            if (!Uri.TryCreate(transparencyPage, rawHref, out var candidate) || !IsMachineReadable(candidate))
            {
                continue;
            }

            links.Add(candidate.AbsoluteUri);
        }

        var discovered = links.Select(static link => new Uri(link)).ToList();
        logger.LogInformation("Discovered {Count} machine-readable transparency files from {Page}", discovered.Count, transparencyPage);
        return discovered;
    }

    private static bool IsMachineReadable(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath);
        return extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".json", StringComparison.OrdinalIgnoreCase);
    }
}
