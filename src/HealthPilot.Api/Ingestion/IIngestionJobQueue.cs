namespace HealthPilot.Api.Ingestion;

/// <summary>
/// In-memory, unbounded FIFO queue for ingestion job IDs.
/// Backed by <see cref="System.Threading.Channels.Channel{T}"/> for thread-safe async producer/consumer access.
/// </summary>
public interface IIngestionJobQueue
{
    /// <summary>
    /// Enqueues <paramref name="jobId"/> for processing by <see cref="IngestionJobWorker"/>.
    /// </summary>
    /// <param name="jobId">The <see cref="Models.IngestionJob.Id"/> to enqueue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask EnqueueAsync(long jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Waits for and dequeues the next job ID. Blocks asynchronously until an item is available or
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The next job ID to process.</returns>
    ValueTask<long> DequeueAsync(CancellationToken cancellationToken);
}
