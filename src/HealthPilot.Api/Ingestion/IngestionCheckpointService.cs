using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HealthPilot.Api.Ingestion;

public class IngestionCheckpointService(IConfiguration configuration) : IIngestionCheckpointService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _lock = new(1, 1);

    private TimeSpan RetentionWindow
    {
        get
        {
            var configuredHours = configuration.GetValue<int?>("Ingestion:CheckpointRetentionHours") ?? 168;
            return TimeSpan.FromHours(Math.Max(configuredHours, 1));
        }
    }

    private string CheckpointDirectory =>
        string.IsNullOrWhiteSpace(configuration["Ingestion:CheckpointDirectory"])
            ? Path.Combine(AppContext.BaseDirectory, "ingestion-checkpoints")
            : Path.GetFullPath(configuration["Ingestion:CheckpointDirectory"]!);

    public async Task<IngestionCheckpointRecord> GetOrCreateAsync(string filePath, int batchSize, CancellationToken cancellationToken)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        var key = ComputeCheckpointKey(normalizedPath, batchSize);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(CheckpointDirectory);
            CleanupExpiredInternal(RetentionWindow);

            var existing = await TryReadAsync(key, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var record = new IngestionCheckpointRecord
            {
                CheckpointKey = key,
                FilePath = normalizedPath,
                BatchSize = batchSize,
                RowsProcessed = 0,
                Status = "in_progress",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            await WriteAsync(record, cancellationToken);
            return record;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveProgressAsync(string checkpointKey, int rowsProcessed, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var record = await TryReadAsync(checkpointKey, cancellationToken)
                ?? throw new InvalidOperationException($"Checkpoint '{checkpointKey}' was not found.");

            record.RowsProcessed = rowsProcessed;
            record.Status = "in_progress";
            record.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await WriteAsync(record, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IngestionCheckpointRecord?> GetByKeyAsync(string checkpointKey, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await TryReadAsync(checkpointKey, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<IngestionCheckpointRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit < 1)
        {
            return [];
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(CheckpointDirectory);
            CleanupExpiredInternal(RetentionWindow);

            var files = Directory.EnumerateFiles(CheckpointDirectory, "*.json")
                .Select(path => new FileInfo(path))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(limit)
                .ToList();

            var results = new List<IngestionCheckpointRecord>(files.Count);
            foreach (var file in files)
            {
                await using var stream = File.OpenRead(file.FullName);
                var record = await JsonSerializer.DeserializeAsync<IngestionCheckpointRecord>(stream, JsonOptions, cancellationToken);
                if (record is not null)
                {
                    results.Add(record);
                }
            }

            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken cancellationToken)
    {
        return await CleanupExpiredAsync(RetentionWindow, cancellationToken);
    }

    public async Task<int> CleanupExpiredAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(CheckpointDirectory);
            return CleanupExpiredInternal(retention);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task MarkCompletedAsync(string checkpointKey, int rowsProcessed, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var record = await TryReadAsync(checkpointKey, cancellationToken)
                ?? throw new InvalidOperationException($"Checkpoint '{checkpointKey}' was not found.");

            record.RowsProcessed = rowsProcessed;
            record.Status = "completed";
            record.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await WriteAsync(record, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IngestionCheckpointRecord?> TryReadAsync(string checkpointKey, CancellationToken cancellationToken)
    {
        var filePath = BuildCheckpointFilePath(checkpointKey);
        if (!File.Exists(filePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<IngestionCheckpointRecord>(stream, JsonOptions, cancellationToken);
    }

    private async Task WriteAsync(IngestionCheckpointRecord record, CancellationToken cancellationToken)
    {
        var filePath = BuildCheckpointFilePath(record.CheckpointKey);
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, record, JsonOptions, cancellationToken);
    }

    private string BuildCheckpointFilePath(string checkpointKey)
    {
        return Path.Combine(CheckpointDirectory, $"{checkpointKey}.json");
    }

    private static string ComputeCheckpointKey(string normalizedFilePath, int batchSize)
    {
        var content = $"{normalizedFilePath}|{batchSize}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private int CleanupExpiredInternal(TimeSpan retention)
    {
        var retentionSafe = retention <= TimeSpan.Zero ? TimeSpan.FromHours(1) : retention;
        var cutoffUtc = DateTimeOffset.UtcNow - retentionSafe;
        var deleted = 0;

        foreach (var filePath in Directory.EnumerateFiles(CheckpointDirectory, "*.json"))
        {
            var lastWrite = File.GetLastWriteTimeUtc(filePath);
            if (lastWrite < cutoffUtc.UtcDateTime)
            {
                File.Delete(filePath);
                deleted++;
            }
        }

        return deleted;
    }
}