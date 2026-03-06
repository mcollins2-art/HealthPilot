namespace HealthPilot.Api.Services;

/// <summary>
/// Immutable input record for benefit simulation. All monetary values must be non-negative;
/// <see cref="CoinsurancePercent"/> must be in the range [0, 100].
/// </summary>
/// <param name="NegotiatedRate">The agreed-upon rate between insurer and provider.</param>
/// <param name="DeductibleRemaining">The member's remaining deductible balance before coinsurance kicks in.</param>
/// <param name="CoinsurancePercent">The member's coinsurance percentage (0–100), applied after the deductible.</param>
/// <param name="Copay">Fixed dollar copay, applied before or after deductible depending on plan design.</param>
/// <param name="OopMaxRemaining">The member's remaining out-of-pocket maximum; caps total patient responsibility.</param>
/// <param name="CopayAppliesBeforeDeductible">
/// When <c>true</c> (default), copay is applied first and the remainder is processed through deductible and coinsurance.
/// When <c>false</c>, deductible is applied first then coinsurance, with copay added to the total.
/// </param>
public record BenefitSimulationInput(
    decimal NegotiatedRate,
    decimal DeductibleRemaining,
    decimal CoinsurancePercent,
    decimal Copay,
    decimal OopMaxRemaining,
    bool CopayAppliesBeforeDeductible = true
);

/// <summary>
/// Output of benefit simulation containing the patient's estimated cost and the insurer's estimated payment.
/// Both values are rounded to two decimal places per <see cref="MonetaryPolicy"/>.
/// </summary>
/// <param name="EstimatedPatientResponsibility">
/// The patient's estimated out-of-pocket cost for this service, capped by <c>OopMaxRemaining</c>.
/// </param>
/// <param name="InsurerPayment">The insurer's estimated payment, equal to the negotiated rate minus patient responsibility.</param>
public record BenefitSimulationResult(
    decimal EstimatedPatientResponsibility,
    decimal InsurerPayment
);
