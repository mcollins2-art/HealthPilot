namespace HealthPilot.Api.Services;

/// <summary>
/// Deletes stale negotiated-rate and cash-price records that have exceeded the configured
/// retention window, keeping the pricing tables bounded in size.
/// </summary>
public interface IPricingLifecycleService
{
    /// <summary>
    /// Deletes pricing records (negotiated rates and cash prices) whose
    /// <c>LastUpdated</c> timestamp is older than the specified retention window.
    /// </summary>
    /// <param name="retention">Maximum age to retain; records older than this are deleted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Counts of deleted negotiated-rate and cash-price rows.</returns>
    Task<(int NegotiatedRatesDeleted, int CashPricesDeleted)> CleanupStalePricingAsync(TimeSpan retention, CancellationToken cancellationToken);
}
