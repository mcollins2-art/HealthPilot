using System.Threading.Channels;
using HealthPilot.Api.Data;
using HealthPilot.Api.Dtos;
using HealthPilot.Api.Ingestion;
using HealthPilot.Api.Models;
using HealthPilot.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HealthPilot.Api.Tests;

public class IngestionJobWorkerTests : IDisposable
{
    private readonly string _tempDirectory;

    public IngestionJobWorkerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "healthpilot-worker-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task Worker_MarksJobDeadLettered_AfterMaxAttempts()
    {
        var csvPath = WriteSingleRowCsv("dead-letter.csv");
        var queue = new RecordingQueue();
        using var provider = CreateProvider(queue, failuresBeforeSuccess: int.MaxValue);
        var worker = new IngestionJobWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            queue,
            NullLogger<IngestionJobWorker>.Instance);

        var jobId = await SeedQueuedJobAsync(provider, csvPath, maxAttempts: 2);
        await queue.EnqueueAsync(jobId, CancellationToken.None);

        await worker.StartAsync(CancellationToken.None);

        var job = await WaitForJobAsync(provider, jobId, x => x.Status == "dead_lettered", TimeSpan.FromSeconds(5));

        await worker.StopAsync(CancellationToken.None);

        Assert.NotNull(job);
        Assert.Equal("dead_lettered", job!.Status);
        Assert.Equal(2, job.AttemptCount);
    }

    [Fact]
    public async Task Worker_RetriesOnce_AndCompletes_WhenFailureIsTransient()
    {
        var csvPath = WriteSingleRowCsv("transient.csv");
        var queue = new RecordingQueue();
        using var provider = CreateProvider(queue, failuresBeforeSuccess: 1);
        var worker = new IngestionJobWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            queue,
            NullLogger<IngestionJobWorker>.Instance);

        var jobId = await SeedQueuedJobAsync(provider, csvPath, maxAttempts: 2);
        await queue.EnqueueAsync(jobId, CancellationToken.None);

        await worker.StartAsync(CancellationToken.None);

        var job = await WaitForJobAsync(provider, jobId, x => x.Status == "completed", TimeSpan.FromSeconds(5));

        await worker.StopAsync(CancellationToken.None);

        Assert.NotNull(job);
        Assert.Equal("completed", job!.Status);
        Assert.Equal(1, job.AttemptCount);
        Assert.Equal(1, job.RecordsReceived);
    }

    [Fact]
    public async Task StartupRecovery_EnqueuesPersistedQueuedJobs()
    {
        var queue = new RecordingQueue();
        using var provider = CreateProvider(queue, failuresBeforeSuccess: 0);

        var queuedJobId = await SeedQueuedJobAsync(provider, WriteSingleRowCsv("queued-recovery.csv"), maxAttempts: 2);
        await SeedCompletedJobAsync(provider, WriteSingleRowCsv("completed-recovery.csv"));

        var recovery = new IngestionJobStartupRecoveryService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            queue,
            NullLogger<IngestionJobStartupRecoveryService>.Instance);

        await recovery.StartAsync(CancellationToken.None);

        Assert.Contains(queuedJobId, queue.EnqueuedJobIds);
        Assert.Single(queue.EnqueuedJobIds);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private ServiceProvider CreateProvider(RecordingQueue queue, int failuresBeforeSuccess)
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddSingleton<IPricingPersistenceService>(new FlakyPricingPersistenceService(failuresBeforeSuccess));
        services.AddScoped<IIngestionCheckpointService, InMemoryCheckpointService>();
        services.AddScoped<PricingIngestionPipeline>();
        services.AddSingleton<IIngestionJobQueue>(queue);
        return services.BuildServiceProvider();
    }

    private string WriteSingleRowCsv(string fileName)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        File.WriteAllText(path,
            "cpt_code,description,category,facility_name,facility_type,city,state,zip,insurer,negotiated_rate,rate_type,cash_price\n" +
            "70551,Brain MRI,imaging,Test Hospital,hospital,Hoboken,NJ,07030,Plan A,1200,contracted,950");
        return path;
    }

    private static async Task<long> SeedQueuedJobAsync(ServiceProvider provider, string filePath, int maxAttempts)
    {
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new IngestionJob
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Status = "queued",
            FilePath = filePath,
            BatchSize = 1,
            ResumeFromCheckpoint = false,
            MaxAttempts = maxAttempts
        };
        dbContext.IngestionJobs.Add(job);
        await dbContext.SaveChangesAsync();
        return job.Id;
    }

    private static async Task SeedCompletedJobAsync(ServiceProvider provider, string filePath)
    {
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new IngestionJob
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Status = "completed",
            FilePath = filePath,
            BatchSize = 1,
            ResumeFromCheckpoint = false
        };
        dbContext.IngestionJobs.Add(job);
        await dbContext.SaveChangesAsync();
    }

    private static async Task<IngestionJob?> WaitForJobAsync(
        ServiceProvider provider,
        long jobId,
        Func<IngestionJob, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var scope = provider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await dbContext.IngestionJobs.AsNoTracking().SingleAsync(x => x.Id == jobId);
            if (predicate(job))
            {
                return job;
            }

            await Task.Delay(50);
        }

        return null;
    }

    private sealed class RecordingQueue : IIngestionJobQueue
    {
        private readonly Channel<long> _channel = Channel.CreateUnbounded<long>();
        public List<long> EnqueuedJobIds { get; } = [];

        public ValueTask EnqueueAsync(long jobId, CancellationToken cancellationToken)
        {
            lock (EnqueuedJobIds)
            {
                EnqueuedJobIds.Add(jobId);
            }

            return _channel.Writer.WriteAsync(jobId, cancellationToken);
        }

        public ValueTask<long> DequeueAsync(CancellationToken cancellationToken)
        {
            return _channel.Reader.ReadAsync(cancellationToken);
        }
    }

    private sealed class FlakyPricingPersistenceService(int failuresBeforeSuccess) : IPricingPersistenceService
    {
        private int _remainingFailures = failuresBeforeSuccess;

        public Task<PricingPersistenceResult> UpsertPricingDataAsync(
            IReadOnlyList<StructuredPricingRecord> records,
            CancellationToken cancellationToken)
        {
            var current = Volatile.Read(ref _remainingFailures);
            if (current > 0)
            {
                Interlocked.Decrement(ref _remainingFailures);
                throw new InvalidOperationException("Transient persistence failure for testing.");
            }

            return Task.FromResult(new PricingPersistenceResult
            {
                RecordsReceived = records.Count,
                NegotiatedRatesUpserted = records.Count,
                CashPricesUpserted = records.Count
            });
        }
    }

    private sealed class InMemoryCheckpointService : IIngestionCheckpointService
    {
        private readonly Dictionary<string, IngestionCheckpointRecord> _checkpoints = new(StringComparer.OrdinalIgnoreCase);

        public Task<IngestionCheckpointRecord> GetOrCreateAsync(string filePath, int batchSize, CancellationToken cancellationToken)
        {
            var existing = _checkpoints.Values.FirstOrDefault(x => x.FilePath == filePath);
            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            var checkpoint = new IngestionCheckpointRecord
            {
                CheckpointKey = Guid.NewGuid().ToString("N"),
                FilePath = filePath,
                BatchSize = batchSize,
                RowsProcessed = 0,
                Status = "in_progress",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            _checkpoints[checkpoint.CheckpointKey] = checkpoint;
            return Task.FromResult(checkpoint);
        }

        public Task<IngestionCheckpointRecord?> GetByKeyAsync(string checkpointKey, CancellationToken cancellationToken)
        {
            _checkpoints.TryGetValue(checkpointKey, out var checkpoint);
            return Task.FromResult(checkpoint);
        }

        public Task<IReadOnlyList<IngestionCheckpointRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken)
        {
            IReadOnlyList<IngestionCheckpointRecord> checkpoints = _checkpoints.Values.Take(limit).ToList();
            return Task.FromResult(checkpoints);
        }

        public Task<int> CleanupExpiredAsync(CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<int> CleanupExpiredAsync(TimeSpan retention, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task SaveProgressAsync(string checkpointKey, int rowsProcessed, CancellationToken cancellationToken)
        {
            if (_checkpoints.TryGetValue(checkpointKey, out var checkpoint))
            {
                checkpoint.RowsProcessed = rowsProcessed;
                checkpoint.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            return Task.CompletedTask;
        }

        public Task MarkCompletedAsync(string checkpointKey, int rowsProcessed, CancellationToken cancellationToken)
        {
            if (_checkpoints.TryGetValue(checkpointKey, out var checkpoint))
            {
                checkpoint.RowsProcessed = rowsProcessed;
                checkpoint.Status = "completed";
                checkpoint.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            return Task.CompletedTask;
        }
    }
}
