using HealthPilot.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.Metrics;

namespace HealthPilot.Api.Ingestion;

public sealed class IngestionJobWorker(
    IServiceScopeFactory scopeFactory,
    IIngestionJobQueue queue,
    ILogger<IngestionJobWorker> logger) : BackgroundService
{
    private readonly string _workerId = $"worker-{Environment.MachineName}-{Guid.NewGuid():N}";
    private static readonly Meter Meter = new("HealthPilot.Ingestion");
    private static readonly Counter<long> JobsCompletedCounter = Meter.CreateCounter<long>("ingestion_jobs_completed");
    private static readonly Counter<long> JobsDeadLetteredCounter = Meter.CreateCounter<long>("ingestion_jobs_dead_lettered");
    private static readonly Histogram<double> QueueLatencyMs = Meter.CreateHistogram<double>("ingestion_queue_latency_ms");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            long? jobId;
            try
            {
                jobId = await TryGetNextJobIdAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (!jobId.HasValue)
            {
                continue;
            }

            try
            {
                await ProcessJobAsync(jobId.Value, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled ingestion worker failure for job {JobId}", jobId.Value);
            }
        }
    }

    private async Task<long?> TryGetNextJobIdAsync(CancellationToken cancellationToken)
    {
        var dequeueTask = queue.DequeueAsync(cancellationToken).AsTask();
        var completedTask = await Task.WhenAny(dequeueTask, Task.Delay(TimeSpan.FromSeconds(1), cancellationToken));
        if (completedTask == dequeueTask)
        {
            return await dequeueTask;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;

        var candidateId = await dbContext.IngestionJobs
            .Where(x => x.Status == "queued" && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc < now))
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => (long?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!candidateId.HasValue)
        {
            return null;
        }

        var updated = await dbContext.IngestionJobs
            .Where(x => x.Id == candidateId.Value && x.Status == "queued" && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc < now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.LeaseOwner, _workerId)
                .SetProperty(x => x.LeaseExpiresAtUtc, now.AddMinutes(5))
                .SetProperty(x => x.Status, "in_progress")
                .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);

        return updated == 1 ? candidateId.Value : null;
    }

    private async Task ProcessJobAsync(long jobId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pipeline = scope.ServiceProvider.GetRequiredService<PricingIngestionPipeline>();

        var job = await dbContext.IngestionJobs.SingleOrDefaultAsync(x => x.Id == jobId, cancellationToken);
        if (job is null)
        {
            return;
        }

        if (job.Status is "completed" or "dead_lettered")
        {
            return;
        }

        if (job.Status == "queued")
        {
            var now = DateTimeOffset.UtcNow;
            var claimed = await dbContext.IngestionJobs
                .Where(x => x.Id == job.Id && x.Status == "queued" && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc < now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.LeaseOwner, _workerId)
                    .SetProperty(x => x.LeaseExpiresAtUtc, now.AddMinutes(5))
                    .SetProperty(x => x.Status, "in_progress")
                    .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);

            if (claimed != 1)
            {
                return;
            }

            job = await dbContext.IngestionJobs.SingleAsync(x => x.Id == jobId, cancellationToken);
        }

        QueueLatencyMs.Record((DateTimeOffset.UtcNow - job.CreatedAtUtc).TotalMilliseconds);

        try
        {
            var result = await pipeline.ImportFileWithBatchingAsync(
                job.FilePath,
                job.BatchSize,
                job.ResumeFromCheckpoint,
                job.Id,
                cancellationToken);

            job.Status = "completed";
            job.CheckpointKey = result.CheckpointKey;
            job.RowsProcessed = result.RowsProcessed;
            job.RecordsReceived = result.Persistence.RecordsReceived;
            job.RecordsSkipped = result.Persistence.RecordsSkipped;
            job.ErrorMessage = null;
            job.LeaseOwner = null;
            job.LeaseExpiresAtUtc = null;
            job.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            JobsCompletedCounter.Add(1);
        }
        catch (Exception ex)
        {
            job.AttemptCount += 1;
            job.ErrorMessage = ex.Message;
            job.UpdatedAtUtc = DateTimeOffset.UtcNow;

            if (job.AttemptCount >= job.MaxAttempts)
            {
                job.Status = "dead_lettered";
                job.LeaseOwner = null;
                job.LeaseExpiresAtUtc = null;
                JobsDeadLetteredCounter.Add(1);
            }
            else
            {
                job.Status = "queued";
                job.LeaseOwner = null;
                job.LeaseExpiresAtUtc = null;
                await queue.EnqueueAsync(job.Id, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
