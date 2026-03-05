namespace HealthPilot.Api.Services;

/// <summary>
/// Implements <see cref="IBenefitSimulationService"/> using standard insurance adjudication logic:
/// copay → deductible → coinsurance, capped by the out-of-pocket maximum.
/// </summary>
public class BenefitSimulationService(ILogger<BenefitSimulationService> logger) : IBenefitSimulationService
{
    /// <inheritdoc/>
    public BenefitSimulationResult Simulate(BenefitSimulationInput input)
    {
        // Defensive clamping protects against malformed values even if validation
        // is bypassed in future batch/async workflows.
        var negotiatedRate = Math.Max(input.NegotiatedRate, 0m);
        var deductibleRemaining = Math.Max(input.DeductibleRemaining, 0m);
        var coinsurancePercent = Math.Clamp(input.CoinsurancePercent, 0m, 100m);
        var copay = Math.Max(input.Copay, 0m);
        var oopMaxRemaining = Math.Max(input.OopMaxRemaining, 0m);

        // Edge case: member has already met out-of-pocket maximum.
        if (oopMaxRemaining == 0m)
        {
            var earlyResult = new BenefitSimulationResult(0m, MonetaryPolicy.Round(negotiatedRate));
            logger.LogDebug(
                "OOP max already met. NegotiatedRate={NegotiatedRate}, PatientResponsibility=0, InsurerPayment={InsurerPayment}",
                negotiatedRate,
                earlyResult.InsurerPayment);
            return earlyResult;
        }

        decimal rawPatientResponsibility;

        if (input.CopayAppliesBeforeDeductible)
        {
            // Most benefit designs apply office/service copay first, then process
            // the remaining allowed amount through deductible and coinsurance.
            var copayApplied = Math.Min(copay, negotiatedRate);
            var remainingAfterCopay = negotiatedRate - copayApplied;

            var deductibleApplied = Math.Min(remainingAfterCopay, deductibleRemaining);
            var remainingAfterDeductible = remainingAfterCopay - deductibleApplied;

            var coinsuranceAmount = remainingAfterDeductible * (coinsurancePercent / 100m);
            rawPatientResponsibility = copayApplied + deductibleApplied + coinsuranceAmount;
        }
        else
        {
            // Fallback for plans that apply deductible before copay.
            var deductibleApplied = Math.Min(negotiatedRate, deductibleRemaining);
            var remainingAfterDeductible = negotiatedRate - deductibleApplied;

            var coinsuranceAmount = remainingAfterDeductible * (coinsurancePercent / 100m);
            rawPatientResponsibility = deductibleApplied + coinsuranceAmount + copay;
        }

        var patientResponsibility = Math.Min(rawPatientResponsibility, oopMaxRemaining);

        var insurerPayment = Math.Max(negotiatedRate - patientResponsibility, 0m);

        var result = new BenefitSimulationResult(
            MonetaryPolicy.Round(patientResponsibility),
            MonetaryPolicy.Round(insurerPayment)
        );

        logger.LogDebug(
            "Benefit simulation complete. NegotiatedRate={NegotiatedRate}, CopayAppliesBeforeDeductible={CopayOrder}, PatientResponsibility={PatientResponsibility}, InsurerPayment={InsurerPayment}",
            negotiatedRate,
            input.CopayAppliesBeforeDeductible,
            result.EstimatedPatientResponsibility,
            result.InsurerPayment);

        return result;
    }
}
