using HealthPilot.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
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
    private static readonly Counter<double> RowsPerSecondCounter = Meter.CreateCounter<double>("ingestion_rows_per_second");
    private const int CircuitBreakerFailureThreshold = 3;
    private static readonly TimeSpan CircuitBreakerCooldown = TimeSpan.FromSeconds(10);
    private int _consecutiveDbFailures;
    private DateTimeOffset? _circuitOpenUntil;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_circuitOpenUntil is not null && _circuitOpenUntil > DateTimeOffset.UtcNow)
            {
                await Task.Delay(_circuitOpenUntil.Value - DateTimeOffset.UtcNow, stoppingToken);
                continue;
            }

            long jobId;
            try
            {
                jobId = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to dequeue ingestion job.");
                if (IsDatabaseException(ex))
                {
                    _consecutiveDbFailures++;
                    if (_consecutiveDbFailures >= CircuitBreakerFailureThreshold)
                    {
                        _circuitOpenUntil = DateTimeOffset.UtcNow.Add(CircuitBreakerCooldown);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            try
            {
                await ProcessJobAsync(jobId, stoppingToken);
                _consecutiveDbFailures = 0;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled ingestion worker failure for job {JobId}", jobId);
                if (IsDatabaseException(ex))
                {
                    _consecutiveDbFailures++;
                    if (_consecutiveDbFailures >= CircuitBreakerFailureThreshold)
                    {
                        _circuitOpenUntil = DateTimeOffset.UtcNow.Add(CircuitBreakerCooldown);
                        logger.LogWarning(
                            "Ingestion worker circuit breaker opened for {CooldownSeconds} seconds after {FailureCount} DB failures.",
                            CircuitBreakerCooldown.TotalSeconds,
                            _consecutiveDbFailures);
                    }
                }
            }
        }
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

        job.Status = "in_progress";
        job.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var timer = Stopwatch.StartNew();
            var result = await pipeline.ImportFileWithBatchingAsync(
                job.FilePath,
                job.BatchSize,
                job.ResumeFromCheckpoint,
                cancellationToken);

            job.Status = "completed";
            job.CheckpointKey = result.CheckpointKey;
            job.RowsProcessed = result.RowsProcessed;
            job.RecordsReceived = result.Persistence.RecordsReceived;
            job.RecordsSkipped = result.Persistence.RecordsSkipped + result.ParseErrors.Count;
            job.ErrorMessage = null;
            job.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            JobsCompletedCounter.Add(1);
            var seconds = Math.Max(timer.Elapsed.TotalSeconds, 0.001);
            RowsPerSecondCounter.Add(result.RowsProcessed / seconds);
        }
        catch (Exception ex)
        {
            job.AttemptCount += 1;
            job.ErrorMessage = ex.Message;
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
    }

    private static bool IsDatabaseException(Exception exception)
    {
        if (exception is DbUpdateException or DbException)
        {
            return true;
        }

        return exception.InnerException is not null && IsDatabaseException(exception.InnerException);
    }
}
