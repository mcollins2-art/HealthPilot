using HealthPilot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Ingestion;

public sealed class IngestionJobQueue(IServiceScopeFactory scopeFactory) : IIngestionJobQueue
{
    public async ValueTask EnqueueAsync(long jobId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await dbContext.IngestionJobs.SingleOrDefaultAsync(x => x.Id == jobId, cancellationToken);
        if (job is null)
        {
            return;
        }

        job.Status = "queued";
        job.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<long> DequeueAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var queuedJobId = await dbContext.IngestionJobs
                .AsNoTracking()
                .Where(x => x.Status == "queued")
                .OrderBy(x => x.CreatedAtUtc)
                .Select(x => (long?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (queuedJobId.HasValue)
            {
                var claimedRows = await dbContext.IngestionJobs
                    .Where(x => x.Id == queuedJobId.Value && x.Status == "queued")
                    .ExecuteUpdateAsync(updates => updates
                        .SetProperty(x => x.Status, "in_progress")
                        .SetProperty(x => x.UpdatedAtUtc, DateTimeOffset.UtcNow), cancellationToken);

                if (claimedRows == 1)
                {
                    return queuedJobId.Value;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new OperationCanceledException(cancellationToken);
    }
}
