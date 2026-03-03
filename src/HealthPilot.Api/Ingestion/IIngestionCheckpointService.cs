namespace HealthPilot.Api.Ingestion;

public interface IIngestionCheckpointService
{
    Task<IngestionCheckpointRecord> GetOrCreateAsync(string filePath, int batchSize, CancellationToken cancellationToken);
    Task<IngestionCheckpointRecord?> GetByKeyAsync(string checkpointKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<IngestionCheckpointRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken);
    Task<int> CleanupExpiredAsync(CancellationToken cancellationToken);
    Task<int> CleanupExpiredAsync(TimeSpan retention, CancellationToken cancellationToken);
    Task SaveProgressAsync(string checkpointKey, int rowsProcessed, CancellationToken cancellationToken);
    Task MarkCompletedAsync(string checkpointKey, int rowsProcessed, CancellationToken cancellationToken);
}