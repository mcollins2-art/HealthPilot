using System.Threading.Channels;

namespace HealthPilot.Api.Ingestion;

public sealed class IngestionJobQueue : IIngestionJobQueue
{
    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>();

    public ValueTask EnqueueAsync(long jobId, CancellationToken cancellationToken)
    {
        return _channel.Writer.WriteAsync(jobId, cancellationToken);
    }

    public ValueTask<long> DequeueAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }
}
