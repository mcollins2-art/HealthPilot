namespace HealthPilot.Api.Ingestion;

public interface IIngestionJobQueue
{
    long PendingCount { get; }
    ValueTask EnqueueAsync(long jobId, CancellationToken cancellationToken);
    ValueTask<long> DequeueAsync(CancellationToken cancellationToken);
}
