namespace HealthPilot.Api.Services;

/// <summary>
/// Aggregated pricing data for a specific procedure, geography, and (optionally) insurer.
/// All values are nullable; a <c>null</c> indicates the requested dimension was not found in
/// the pricing database.
/// </summary>
/// <param name="NegotiatedMin">Minimum negotiated rate across all matching facilities, or <c>null</c> if no negotiated rates exist.</param>
/// <param name="NegotiatedMax">Maximum negotiated rate across all matching facilities, or <c>null</c> if no negotiated rates exist.</param>
/// <param name="CashMin">Minimum cash (self-pay) price across all matching facilities, or <c>null</c> if no cash prices exist.</param>
/// <param name="CashMax">Maximum cash (self-pay) price across all matching facilities, or <c>null</c> if no cash prices exist.</param>
public record PricingSummary(
    decimal? NegotiatedMin,
    decimal? NegotiatedMax,
    decimal? CashMin,
    decimal? CashMax
);
