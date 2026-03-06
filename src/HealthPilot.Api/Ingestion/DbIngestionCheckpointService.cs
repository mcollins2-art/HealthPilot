using System.Security.Cryptography;
using System.Text;
using HealthPilot.Api.Data;
using HealthPilot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Ingestion;

public class DbIngestionCheckpointService(
    AppDbContext dbContext,
    IConfiguration configuration) : IIngestionCheckpointService
{
    private TimeSpan RetentionWindow
    {
        get
        {
            var configuredHours = configuration.GetValue<int?>("Ingestion:CheckpointRetentionHours") ?? 168;
            return TimeSpan.FromHours(Math.Max(configuredHours, 1));
        }
    }

    public async Task<IngestionCheckpointRecord> GetOrCreateAsync(string filePath, int batchSize, CancellationToken cancellationToken, string? fileHashSha256 = null)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        var key = await ComputeCheckpointKeyAsync(normalizedPath, batchSize, cancellationToken, fileHashSha256);
        var existing = await dbContext.IngestionCheckpoints.SingleOrDefaultAsync(x => x.CheckpointKey == key, cancellationToken);
        if (existing is not null)
        {
            return ToRecord(existing);
        }

        var checkpoint = new IngestionCheckpoint
        {
            CheckpointKey = key,
            FilePath = normalizedPath,
            BatchSize = batchSize,
            RowsProcessed = 0,
            Status = "in_progress",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.IngestionCheckpoints.Add(checkpoint);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToRecord(checkpoint);
    }

    public async Task<IngestionCheckpointRecord?> GetByKeyAsync(string checkpointKey, CancellationToken cancellationToken)
    {
        var checkpoint = await dbContext.IngestionCheckpoints.SingleOrDefaultAsync(x => x.CheckpointKey == checkpointKey, cancellationToken);
        return checkpoint is null ? null : ToRecord(checkpoint);
    }

    public async Task<IReadOnlyList<IngestionCheckpointRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit < 1)
        {
            return [];
        }

        await CleanupExpiredAsync(cancellationToken);

        return await dbContext.IngestionCheckpoints
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(limit)
            .Select(x => ToRecord(x))
            .ToListAsync(cancellationToken);
    }

    public Task<int> CleanupExpiredAsync(CancellationToken cancellationToken)
    {
        return CleanupExpiredAsync(RetentionWindow, cancellationToken);
    }

    public async Task<int> CleanupExpiredAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        var retentionSafe = retention <= TimeSpan.Zero ? TimeSpan.FromHours(1) : retention;
        var cutoff = DateTimeOffset.UtcNow - retentionSafe;
        var expired = await dbContext.IngestionCheckpoints
            .Where(x => x.UpdatedAtUtc < cutoff)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            return 0;
        }

        dbContext.IngestionCheckpoints.RemoveRange(expired);
        await dbContext.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    public async Task SaveProgressAsync(string checkpointKey, int rowsProcessed, CancellationToken cancellationToken)
    {
        var checkpoint = await dbContext.IngestionCheckpoints.SingleOrDefaultAsync(x => x.CheckpointKey == checkpointKey, cancellationToken)
            ?? throw new InvalidOperationException($"Checkpoint '{checkpointKey}' was not found.");

        checkpoint.RowsProcessed = rowsProcessed;
        checkpoint.Status = "in_progress";
        checkpoint.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkCompletedAsync(string checkpointKey, int rowsProcessed, CancellationToken cancellationToken)
    {
        var checkpoint = await dbContext.IngestionCheckpoints.SingleOrDefaultAsync(x => x.CheckpointKey == checkpointKey, cancellationToken)
            ?? throw new InvalidOperationException($"Checkpoint '{checkpointKey}' was not found.");

        checkpoint.RowsProcessed = rowsProcessed;
        checkpoint.Status = "completed";
        checkpoint.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static IngestionCheckpointRecord ToRecord(IngestionCheckpoint checkpoint)
    {
        return new IngestionCheckpointRecord
        {
            CheckpointKey = checkpoint.CheckpointKey,
            FilePath = checkpoint.FilePath,
            BatchSize = checkpoint.BatchSize,
            RowsProcessed = checkpoint.RowsProcessed,
            Status = checkpoint.Status,
            UpdatedAtUtc = checkpoint.UpdatedAtUtc
        };
    }

    private static async Task<string> ComputeCheckpointKeyAsync(
        string normalizedFilePath,
        int batchSize,
        CancellationToken cancellationToken,
        string? fileHashSha256)
    {
        string content;
        if (!string.IsNullOrWhiteSpace(fileHashSha256))
        {
            content = $"{fileHashSha256}|{batchSize}";
        }
        else if (File.Exists(normalizedFilePath))
        {
            await using var stream = File.OpenRead(normalizedFilePath);
            var fileHash = await SHA256.HashDataAsync(stream, cancellationToken);
            content = $"{Convert.ToHexString(fileHash)}|{batchSize}";
        }
        else
        {
            content = $"{normalizedFilePath}|{batchSize}";
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
