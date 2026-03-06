namespace HealthPilot.Api.Ingestion;

/// <summary>
/// Manages durable, database-backed checkpoints for batched pricing file imports.
/// Checkpoints enable resume-from-last-row on restart and expose progress visibility
/// through the ingestion control-plane endpoints.
/// </summary>
public interface IIngestionCheckpointService
{
    /// <summary>
    /// Returns an existing checkpoint for the given file path and batch size, or creates a new one.
    /// </summary>
    /// <param name="filePath">Absolute path to the file being imported.</param>
    /// <param name="batchSize">Batch size used for this import run (part of the checkpoint key).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The existing or newly created <see cref="IngestionCheckpointRecord"/>.</returns>
    Task<IngestionCheckpointRecord> GetOrCreateAsync(string filePath, int batchSize, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a checkpoint by its opaque key, or <c>null</c> if not found.
    /// </summary>
    /// <param name="checkpointKey">SHA-256 hex checkpoint key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IngestionCheckpointRecord?> GetByKeyAsync(string checkpointKey, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the most recently updated checkpoints up to <paramref name="limit"/>, after purging expired entries.
    /// </summary>
    /// <param name="limit">Maximum number of records to return (1–200).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<IngestionCheckpointRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes checkpoints older than the configured retention window.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of deleted checkpoints.</returns>
    Task<int> CleanupExpiredAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes checkpoints older than <paramref name="retention"/>.
    /// </summary>
    /// <param name="retention">Maximum age to retain; checkpoints older than this are deleted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of deleted checkpoints.</returns>
    Task<int> CleanupExpiredAsync(TimeSpan retention, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the <c>RowsProcessed</c> counter for the specified checkpoint.
    /// </summary>
    /// <param name="checkpointKey">SHA-256 hex checkpoint key.</param>
    /// <param name="rowsProcessed">Total rows processed so far (used as resume offset).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveProgressAsync(string checkpointKey, int rowsProcessed, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the specified checkpoint as <c>"completed"</c> and records the final row count.
    /// </summary>
    /// <param name="checkpointKey">SHA-256 hex checkpoint key.</param>
    /// <param name="rowsProcessed">Final total rows processed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkCompletedAsync(string checkpointKey, int rowsProcessed, CancellationToken cancellationToken);
}