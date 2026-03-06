namespace HealthPilot.Api.Services;

public interface IPricingLifecycleService
{
    Task<(int NegotiatedRatesCount, int CashPricesCount)> GetStalePricingCountsAsync(TimeSpan retention, CancellationToken cancellationToken);
    Task<(int NegotiatedRatesDeleted, int CashPricesDeleted)> CleanupStalePricingAsync(TimeSpan retention, CancellationToken cancellationToken);
}
