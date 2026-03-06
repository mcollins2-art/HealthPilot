namespace HealthPilot.Api.Dtos;

/// <summary>
/// Response body for the <c>POST /estimate</c> endpoint.
/// Contains negotiated and cash price aggregates plus the benefit-simulation output.
/// </summary>
public class EstimateResponse
{
    /// <summary>Minimum negotiated rate found across matching facilities, or <c>null</c> if unavailable.</summary>
    public decimal? NegotiatedRateMin { get; set; }

    /// <summary>Maximum negotiated rate found across matching facilities, or <c>null</c> if unavailable.</summary>
    public decimal? NegotiatedRateMax { get; set; }

    /// <summary>Human-readable negotiated rate range (e.g. <c>"$900.00 - $1400.00"</c>), or <c>"N/A"</c>.</summary>
    public required string NegotiatedRateRange { get; set; }

    /// <summary>Patient's estimated out-of-pocket responsibility, rounded to two decimal places.</summary>
    public decimal EstimatedOutOfPocket { get; set; }

    /// <summary>Minimum cash (self-pay) price found across matching facilities, or <c>null</c> if unavailable.</summary>
    public decimal? CashPriceMin { get; set; }

    /// <summary>Maximum cash (self-pay) price found across matching facilities, or <c>null</c> if unavailable.</summary>
    public decimal? CashPriceMax { get; set; }

    /// <summary>Human-readable cash price range (e.g. <c>"$500.00 - $800.00"</c>), or <c>"N/A"</c>.</summary>
    public required string CashPriceRange { get; set; }

    /// <summary>Insurer's estimated payment, rounded to two decimal places.</summary>
    public decimal InsurerPaymentEstimate { get; set; }

    /// <summary>
    /// Name of the <see cref="Services.MonetaryPolicy.RoundingMode"/> applied to all monetary values in this response.
    /// </summary>
    public required string RoundingMode { get; set; }
}
