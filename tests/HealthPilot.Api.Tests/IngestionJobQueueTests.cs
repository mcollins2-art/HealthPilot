using HealthPilot.Api.Ingestion;
using Xunit;

namespace HealthPilot.Api.Tests;

public class IngestionJobQueueTests
{
    [Fact]
    public async Task PendingCount_IncrementsAndDecrements_WithQueueOperations()
    {
        var queue = new IngestionJobQueue();
        Assert.Equal(0, queue.PendingCount);

        await queue.EnqueueAsync(101, CancellationToken.None);
        await queue.EnqueueAsync(102, CancellationToken.None);
        Assert.Equal(2, queue.PendingCount);

        _ = await queue.DequeueAsync(CancellationToken.None);
        Assert.Equal(1, queue.PendingCount);

        _ = await queue.DequeueAsync(CancellationToken.None);
        Assert.Equal(0, queue.PendingCount);
    }
}
