namespace HealthPilot.Api.Services;

/// <summary>
/// Pricing selection strategy that uses the minimum observed negotiated rate
/// as the representative value for benefit simulation.
/// </summary>
/// <remarks>
/// Using the minimum rate gives patients a best-case estimate, which is the
/// most transparent and consumer-friendly approach for price transparency tools.
/// </remarks>
public sealed class NegotiatedMinPricingSelectionStrategy : IPricingSelectionStrategy
{
    /// <summary>Stable identifier for this strategy used in audit log entries.</summary>
    public const string Version = "negotiated_min";

    /// <inheritdoc/>
    public string PolicyName => Version;

    /// <inheritdoc/>
    public decimal SelectRepresentativeRate(PricingSummary pricingSummary)
    {
        return pricingSummary.NegotiatedMin ?? 0m;
    }
}
