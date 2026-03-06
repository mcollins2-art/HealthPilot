namespace HealthPilot.Api.Services;

public interface IPricingQueryService
{
    Task<PricingSummary> GetPricingSummaryAsync(string zipCode, string insurer, string cptCode, CancellationToken cancellationToken);
    Task<CheapestProviderSummary?> GetCheapestProviderAsync(string zipCode, string insurer, string cptCode, CancellationToken cancellationToken);
    string FormatRange(decimal? minValue, decimal? maxValue);
}
