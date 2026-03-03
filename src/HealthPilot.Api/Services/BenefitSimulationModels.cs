namespace HealthPilot.Api.Services;

public record BenefitSimulationInput(
    decimal NegotiatedRate,
    decimal DeductibleRemaining,
    decimal CoinsurancePercent,
    decimal Copay,
    decimal OopMaxRemaining,
    bool CopayAppliesBeforeDeductible = true
);

public record BenefitSimulationResult(
    decimal EstimatedPatientResponsibility,
    decimal InsurerPayment
);
