using System.Threading.Channels;

namespace HealthPilot.Api.Services;

/// <summary>
/// Singleton channel that decouples audit-log production (on the HTTP request path)
/// from consumption (the <see cref="BackgroundAuditWriter"/> background service).
/// Using an unbounded channel ensures the write side never blocks; the background
/// writer drains it asynchronously without adding latency to the estimate response.
/// </summary>
public sealed class BackgroundAuditChannel
{
    private readonly Channel<AuditItem> _channel =
        Channel.CreateUnbounded<AuditItem>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Write side used by <see cref="EstimateAuditService"/>.</summary>
    internal ChannelWriter<AuditItem> Writer => _channel.Writer;

    /// <summary>Read side used by <see cref="BackgroundAuditWriter"/>.</summary>
    internal ChannelReader<AuditItem> Reader => _channel.Reader;
}
