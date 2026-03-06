namespace HealthPilot.Api.Services;

/// <summary>
/// Defines the monetary rounding rules applied uniformly across all pricing calculations.
/// All money values stored and returned by this API are rounded to two decimal places.
/// </summary>
public static class MonetaryPolicy
{
    /// <summary>Number of decimal places used for all monetary rounding.</summary>
    public const int Scale = 2;

    /// <summary>Midpoint rounding strategy; halves round away from zero.</summary>
    public const MidpointRounding RoundingMode = MidpointRounding.AwayFromZero;

    /// <summary>
    /// Version string embedded in audit log entries to allow future policy changes to be
    /// identified and back-calculated against historical data.
    /// </summary>
    public const string PolicyVersion = "1.1";

    /// <summary>Rounds <paramref name="amount"/> to <see cref="Scale"/> decimal places using <see cref="RoundingMode"/>.</summary>
    /// <param name="amount">The monetary amount to round.</param>
    /// <returns>The rounded value.</returns>
    public static decimal Round(decimal amount) => decimal.Round(amount, Scale, RoundingMode);
}
