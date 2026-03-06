using System.Threading.Channels;

namespace HealthPilot.Api.Ingestion;

public sealed class IngestionJobQueue : IIngestionJobQueue
{
    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>();
    private long _pendingCount;

    public long PendingCount => Interlocked.Read(ref _pendingCount);

    public async ValueTask EnqueueAsync(long jobId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _pendingCount);
        try
        {
            await _channel.Writer.WriteAsync(jobId, cancellationToken);
        }
        catch
        {
            Interlocked.Decrement(ref _pendingCount);
            throw;
        }
    }

    public ValueTask<long> DequeueAsync(CancellationToken cancellationToken)
    {
        return DequeueAndTrackAsync(cancellationToken);
    }

    private async ValueTask<long> DequeueAndTrackAsync(CancellationToken cancellationToken)
    {
        var jobId = await _channel.Reader.ReadAsync(cancellationToken);
        Interlocked.Decrement(ref _pendingCount);
        return jobId;
    }
}
