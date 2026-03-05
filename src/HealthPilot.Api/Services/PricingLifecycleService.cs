using HealthPilot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Services;

/// <summary>
/// Deletes stale negotiated-rate and cash-price records from the database to prevent
/// unbounded table growth. Uses EF Core's <c>ExecuteDeleteAsync</c> for efficient bulk deletes.
/// </summary>
public class PricingLifecycleService(AppDbContext dbContext, ILogger<PricingLifecycleService> logger) : IPricingLifecycleService
{
    /// <inheritdoc/>
    public async Task<(int NegotiatedRatesDeleted, int CashPricesDeleted)> CleanupStalePricingAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        var retentionSafe = retention <= TimeSpan.Zero ? TimeSpan.FromDays(365) : retention;
        var cutoff = DateTimeOffset.UtcNow - retentionSafe;
        var negotiatedDeleted = await dbContext.NegotiatedRates
            .Where(x => x.LastUpdated < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var cashDeleted = await dbContext.CashPrices
            .Where(x => x.LastUpdated < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        logger.LogInformation(
            "Pricing lifecycle cleanup complete. RetentionDays={RetentionDays}, Cutoff={Cutoff}, NegotiatedRatesDeleted={NegotiatedRatesDeleted}, CashPricesDeleted={CashPricesDeleted}",
            retentionSafe.TotalDays,
            cutoff,
            negotiatedDeleted,
            cashDeleted);

        return (negotiatedDeleted, cashDeleted);
    }
}
