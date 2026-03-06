using HealthPilot.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HealthPilot.Api.Ingestion;

public sealed class IngestionJobWorker(
    IServiceScopeFactory scopeFactory,
    IIngestionJobQueue queue,
    ILogger<IngestionJobWorker> logger) : BackgroundService
{
    private static readonly Meter Meter = new("HealthPilot.Ingestion");
    private static readonly Counter<long> JobsCompletedCounter = Meter.CreateCounter<long>("ingestion_jobs_completed");
    private static readonly Counter<long> JobsDeadLetteredCounter = Meter.CreateCounter<long>("ingestion_jobs_dead_lettered");
    private static readonly Histogram<double> JobQueueLagSecondsHistogram = Meter.CreateHistogram<double>("ingestion_job_queue_lag_seconds");
    private static readonly Histogram<double> JobDurationSecondsHistogram = Meter.CreateHistogram<double>("ingestion_job_duration_seconds");
    private readonly ObservableGauge<long> _queueDepthGauge = Meter.CreateObservableGauge(
        "ingestion_jobs_queue_depth",
        () => queue.PendingCount);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = _queueDepthGauge;

        while (!stoppingToken.IsCancellationRequested)
        {
            long jobId;
            try
            {
                jobId = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await ProcessJobAsync(jobId, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled ingestion worker failure for job {JobId}", jobId);
            }
        }
    }

    private async Task ProcessJobAsync(long jobId, CancellationToken cancellationToken)
    {
        var jobDuration = Stopwatch.StartNew();
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

        var queueLag = (DateTimeOffset.UtcNow - job.CreatedAtUtc).TotalSeconds;
        if (queueLag >= 0)
        {
            JobQueueLagSecondsHistogram.Record(queueLag);
        }

        job.Status = "in_progress";
        job.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var result = await pipeline.ImportFileWithBatchingAsync(
                job.FilePath,
                job.BatchSize,
                job.ResumeFromCheckpoint,
                cancellationToken);

            job.Status = "completed";
            job.CheckpointKey = result.CheckpointKey;
            job.RowsProcessed = result.RowsProcessed;
            job.RecordsReceived = result.Persistence.RecordsReceived;
            job.RecordsSkipped = result.Persistence.RecordsSkipped;
            job.ErrorMessage = null;
            job.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            JobsCompletedCounter.Add(1);
        }
        catch (Exception ex)
        {
            job.AttemptCount += 1;
            var correlationId = Activity.Current?.Id ?? $"job-{job.Id}-attempt-{job.AttemptCount}";
            logger.LogWarning(ex, "Ingestion job processing failed for job {JobId}, correlationId={CorrelationId}", job.Id, correlationId);
            job.ErrorMessage = IngestionErrorFormatter.BuildBoundedErrorMessage(ex, correlationId);
            job.UpdatedAtUtc = DateTimeOffset.UtcNow;

            if (job.AttemptCount >= job.MaxAttempts)
            {
                job.Status = "dead_lettered";
                JobsDeadLetteredCounter.Add(1);
            }
            else
            {
                job.Status = "queued";
                await queue.EnqueueAsync(job.Id, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            JobDurationSecondsHistogram.Record(jobDuration.Elapsed.TotalSeconds);
        }
    }
}
