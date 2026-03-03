using HealthPilot.Api.Ingestion;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HealthPilot.Api.Tests;

public class IngestionCheckpointServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly IIngestionCheckpointService _service;

    public IngestionCheckpointServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "healthpilot-checkpoints-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ingestion:CheckpointDirectory"] = _tempDirectory,
                ["Ingestion:CheckpointRetentionHours"] = "168"
            })
            .Build();

        _service = new IngestionCheckpointService(configuration);
    }

    [Fact]
    public async Task GetOrCreateAsync_CreatesCheckpoint_WhenMissing()
    {
        var record = await _service.GetOrCreateAsync("C:\\data\\file.csv", 5000, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(record.CheckpointKey));
        Assert.Equal(Path.GetFullPath("C:\\data\\file.csv"), record.FilePath);
        Assert.Equal(5000, record.BatchSize);
        Assert.Equal(0, record.RowsProcessed);
        Assert.Equal("in_progress", record.Status);

        var files = Directory.GetFiles(_tempDirectory, "*.json");
        Assert.Single(files);
    }

    [Fact]
    public async Task GetOrCreateAsync_ReturnsExistingCheckpoint_WhenAlreadyPresent()
    {
        var first = await _service.GetOrCreateAsync("C:\\data\\same.csv", 2000, CancellationToken.None);
        await _service.SaveProgressAsync(first.CheckpointKey, 1234, CancellationToken.None);

        var second = await _service.GetOrCreateAsync("C:\\data\\same.csv", 2000, CancellationToken.None);

        Assert.Equal(first.CheckpointKey, second.CheckpointKey);
        Assert.Equal(1234, second.RowsProcessed);
    }

    [Fact]
    public async Task SaveProgressAsync_UpdatesRowsAndKeepsInProgressStatus()
    {
        var record = await _service.GetOrCreateAsync("C:\\data\\progress.csv", 1000, CancellationToken.None);

        await _service.SaveProgressAsync(record.CheckpointKey, 777, CancellationToken.None);
        var updated = await _service.GetByKeyAsync(record.CheckpointKey, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal(777, updated!.RowsProcessed);
        Assert.Equal("in_progress", updated.Status);
    }

    [Fact]
    public async Task MarkCompletedAsync_SetsCompletedStatusAndRowsProcessed()
    {
        var record = await _service.GetOrCreateAsync("C:\\data\\done.csv", 1500, CancellationToken.None);

        await _service.MarkCompletedAsync(record.CheckpointKey, 9000, CancellationToken.None);
        var updated = await _service.GetByKeyAsync(record.CheckpointKey, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("completed", updated!.Status);
        Assert.Equal(9000, updated.RowsProcessed);
    }

    [Fact]
    public async Task GetByKeyAsync_ReturnsNull_WhenCheckpointMissing()
    {
        var result = await _service.GetByKeyAsync("missing-key", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListRecentAsync_ReturnsMostRecentFirst_RespectsLimit()
    {
        var first = await _service.GetOrCreateAsync("C:\\data\\first.csv", 1000, CancellationToken.None);
        await _service.SaveProgressAsync(first.CheckpointKey, 10, CancellationToken.None);
        await Task.Delay(25);

        var second = await _service.GetOrCreateAsync("C:\\data\\second.csv", 1000, CancellationToken.None);
        await _service.SaveProgressAsync(second.CheckpointKey, 20, CancellationToken.None);
        await Task.Delay(25);

        var third = await _service.GetOrCreateAsync("C:\\data\\third.csv", 1000, CancellationToken.None);
        await _service.SaveProgressAsync(third.CheckpointKey, 30, CancellationToken.None);

        var recent = await _service.ListRecentAsync(2, CancellationToken.None);

        Assert.Equal(2, recent.Count);
        Assert.Equal(third.CheckpointKey, recent[0].CheckpointKey);
        Assert.Equal(second.CheckpointKey, recent[1].CheckpointKey);
    }

    [Fact]
    public async Task CleanupExpiredAsync_DeletesOnlyExpiredFiles()
    {
        var oldCheckpoint = await _service.GetOrCreateAsync("C:\\data\\old.csv", 1000, CancellationToken.None);
        var freshCheckpoint = await _service.GetOrCreateAsync("C:\\data\\fresh.csv", 1000, CancellationToken.None);

        var oldFile = Path.Combine(_tempDirectory, oldCheckpoint.CheckpointKey + ".json");
        var freshFile = Path.Combine(_tempDirectory, freshCheckpoint.CheckpointKey + ".json");

        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddHours(-10));
        File.SetLastWriteTimeUtc(freshFile, DateTime.UtcNow.AddMinutes(-5));

        var deleted = await _service.CleanupExpiredAsync(TimeSpan.FromHours(1), CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(freshFile));
    }

    [Fact]
    public async Task CleanupExpiredAsync_UsesConfiguredRetention_WhenNoOverride()
    {
        var checkpoint = await _service.GetOrCreateAsync("C:\\data\\configured.csv", 1000, CancellationToken.None);
        var file = Path.Combine(_tempDirectory, checkpoint.CheckpointKey + ".json");

        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-8));

        var deleted = await _service.CleanupExpiredAsync(CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task CleanupExpiredAsync_WithZeroOrNegativeRetention_FallsBackToOneHour()
    {
        var oldCheckpoint = await _service.GetOrCreateAsync("C:\\data\\old2.csv", 1000, CancellationToken.None);
        var file = Path.Combine(_tempDirectory, oldCheckpoint.CheckpointKey + ".json");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-2));

        var deleted = await _service.CleanupExpiredAsync(TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task ListRecentAsync_ReturnsEmpty_WhenLimitInvalid()
    {
        var records = await _service.ListRecentAsync(0, CancellationToken.None);

        Assert.Empty(records);
    }

    [Fact]
    public async Task SaveProgressAsync_Throws_WhenCheckpointMissing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SaveProgressAsync("missing", 1, CancellationToken.None));
    }

    [Fact]
    public async Task MarkCompletedAsync_Throws_WhenCheckpointMissing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.MarkCompletedAsync("missing", 1, CancellationToken.None));
    }

    [Fact]
    public async Task GetOrCreateAsync_UsesDefaultDirectory_WhenConfigMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ingestion:CheckpointRetentionHours"] = "168"
            })
            .Build();

        var service = new IngestionCheckpointService(configuration);
        var filePath = Path.Combine(Path.GetTempPath(), $"healthpilot-default-checkpoint-{Guid.NewGuid():N}.csv");
        var checkpointFile = string.Empty;

        try
        {
            var record = await service.GetOrCreateAsync(filePath, 1000, CancellationToken.None);
            checkpointFile = Path.Combine(AppContext.BaseDirectory, "ingestion-checkpoints", record.CheckpointKey + ".json");

            Assert.True(File.Exists(checkpointFile));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(checkpointFile) && File.Exists(checkpointFile))
            {
                try
                {
                    File.Delete(checkpointFile);
                }
                catch
                {
                }
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }
}
