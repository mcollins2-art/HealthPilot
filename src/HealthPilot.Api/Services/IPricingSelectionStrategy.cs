namespace HealthPilot.Api.Services;

/// <summary>
/// Selects a single representative negotiated rate from a <see cref="PricingSummary"/>
/// to drive benefit simulation.
/// </summary>
public interface IPricingSelectionStrategy
{
    /// <summary>
    /// Gets the unique, versioned name of this selection strategy, used for audit logging.
    /// </summary>
    string PolicyName { get; }

    /// <summary>
    /// Selects the representative negotiated rate from the provided pricing summary.
    /// Returns <c>0</c> when no negotiated data is available.
    /// </summary>
    /// <param name="pricingSummary">Pricing aggregate for the requested geography and procedure.</param>
    /// <returns>The representative rate to use in benefit simulation.</returns>
    decimal SelectRepresentativeRate(PricingSummary pricingSummary);
}
