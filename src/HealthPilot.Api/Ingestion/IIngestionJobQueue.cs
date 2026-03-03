namespace HealthPilot.Api.Ingestion;

public interface IIngestionJobQueue
{
    ValueTask EnqueueAsync(long jobId, CancellationToken cancellationToken);
    ValueTask<long> DequeueAsync(CancellationToken cancellationToken);
}
