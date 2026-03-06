namespace HealthPilot.Api.Services;

/// <summary>
/// Immutable snapshot of all fields needed to persist an <see cref="Models.EstimateAuditLog"/>.
/// Instances are enqueued by <see cref="EstimateAuditService"/> and written to the database
/// by <see cref="BackgroundAuditWriter"/>.
/// </summary>
internal sealed record AuditItem(
    DateTimeOffset CreatedAt,
    string TraceId,
    string ZipCode,
    string Insurer,
    string CptCode,
    decimal NegotiatedRateUsed,
    decimal DeductibleRemaining,
    decimal CoinsurancePercent,
    decimal Copay,
    decimal OopMaxRemaining,
    bool CopayAppliesBeforeDeductible,
    decimal EstimatedPatientResponsibility,
    decimal InsurerPayment,
    string BenefitLogicVersion);
