using System.Threading.Channels;

namespace HealthPilot.Api.Ingestion;

/// <summary>
/// Default implementation of <see cref="IIngestionJobQueue"/> backed by an unbounded
/// <see cref="Channel{T}"/> of <see cref="long"/> job IDs.
/// </summary>
public sealed class IngestionJobQueue : IIngestionJobQueue
{
    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>();

    /// <inheritdoc/>
    public ValueTask EnqueueAsync(long jobId, CancellationToken cancellationToken)
    {
        return _channel.Writer.WriteAsync(jobId, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<long> DequeueAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }
}
