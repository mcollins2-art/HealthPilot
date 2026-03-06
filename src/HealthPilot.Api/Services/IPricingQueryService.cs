namespace HealthPilot.Api.Services;

/// <summary>
/// Retrieves pricing data aggregates (negotiated rates and cash prices) for a given
/// zip-code / insurer / CPT-code combination.
/// </summary>
public interface IPricingQueryService
{
    /// <summary>
    /// Returns the min/max negotiated and cash prices for the requested geography, insurer, and procedure.
    /// </summary>
    /// <param name="zipCode">Five- or nine-digit zip code of the service location.</param>
    /// <param name="insurer">Insurer name as it appears in the pricing data.</param>
    /// <param name="cptCode">CPT procedure code (normalized to uppercase, non-alphanumeric characters removed).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="PricingSummary"/> containing nullable min/max values for negotiated and cash prices.</returns>
    Task<PricingSummary> GetPricingSummaryAsync(string zipCode, string insurer, string cptCode, CancellationToken cancellationToken);

    /// <summary>
    /// Formats a min/max price pair as a human-readable dollar range (e.g. <c>"$900.00 - $1400.00"</c>),
    /// or <c>"N/A"</c> if either value is absent.
    /// </summary>
    /// <param name="minValue">Minimum value, or <c>null</c> if unavailable.</param>
    /// <param name="maxValue">Maximum value, or <c>null</c> if unavailable.</param>
    /// <returns>A formatted price range string.</returns>
    string FormatRange(decimal? minValue, decimal? maxValue);
}
