namespace HealthPilot.Api.Services;

public interface IPricingLifecycleService
{
    Task<(int NegotiatedRatesDeleted, int CashPricesDeleted)> CleanupStalePricingAsync(TimeSpan retention, CancellationToken cancellationToken);
}
