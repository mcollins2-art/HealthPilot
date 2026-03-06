using HealthPilot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Ingestion;

public sealed class IngestionJobStartupRecoveryService(
    IServiceScopeFactory scopeFactory,
    IIngestionJobQueue queue,
    ILogger<IngestionJobStartupRecoveryService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        List<long> queuedJobIds;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            queuedJobIds = await dbContext.IngestionJobs
                .AsNoTracking()
                .Where(x => x.Status == "queued")
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Skipping queued ingestion job recovery at startup.");
            return;
        }

        foreach (var queuedJobId in queuedJobIds)
        {
            await queue.EnqueueAsync(queuedJobId, cancellationToken);
        }

        if (queuedJobIds.Count > 0)
        {
            logger.LogInformation("Recovered {QueuedJobCount} queued ingestion job(s) at startup.", queuedJobIds.Count);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
