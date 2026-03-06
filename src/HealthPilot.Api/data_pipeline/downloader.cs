using Microsoft.Extensions.Logging;

namespace HealthPilot.Api.DataPipeline;

public sealed record DownloadProgress(long BytesDownloaded, long? TotalBytes)
{
    public double? PercentComplete => TotalBytes is > 0 ? (double)BytesDownloaded / TotalBytes.Value * 100d : null;
}

public class Downloader(HttpClient httpClient, ILogger<Downloader> logger)
{
    public async Task<string> DownloadAsync(
        Uri source,
        string destinationPath,
        int maxRetries = 3,
        int chunkSize = 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        if (maxRetries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetries));
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        var tempPath = $"{destinationPath}.part";

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var response = await httpClient.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength;
                long downloaded = 0;

                await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var destinationStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, chunkSize, useAsync: true);

                var buffer = new byte[chunkSize];
                int read;
                while ((read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloaded += read;

                    var progress = new DownloadProgress(downloaded, totalBytes);
                    if (progress.PercentComplete is { } percent)
                    {
                        logger.LogInformation("Downloading {FileName}: {ProgressPercent:F2}% ({BytesDownloaded}/{TotalBytes} bytes)", Path.GetFileName(destinationPath), percent, downloaded, totalBytes);
                    }
                    else
                    {
                        logger.LogInformation("Downloading {FileName}: {BytesDownloaded} bytes", Path.GetFileName(destinationPath), downloaded);
                    }
                }

                File.Move(tempPath, destinationPath, overwrite: true);
                logger.LogInformation("Downloaded {Source} to {Destination}", source, destinationPath);
                return destinationPath;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                if (attempt == maxRetries)
                {
                    logger.LogError(ex, "Failed to download {Source} after {Attempts} attempts", source, maxRetries);
                    throw;
                }

                var backoff = TimeSpan.FromSeconds(attempt);
                logger.LogWarning(ex, "Download attempt {Attempt}/{MaxRetries} failed for {Source}. Retrying in {BackoffSeconds}s", attempt, maxRetries, source, backoff.TotalSeconds);
                await Task.Delay(backoff, cancellationToken);
            }
        }

        throw new InvalidOperationException("Unreachable downloader state.");
    }
}
