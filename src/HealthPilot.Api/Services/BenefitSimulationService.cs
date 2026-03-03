namespace HealthPilot.Api.Services;

public class BenefitSimulationService : IBenefitSimulationService
{
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
            return new BenefitSimulationResult(0m, decimal.Round(negotiatedRate, 2));
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

        return new BenefitSimulationResult(
            decimal.Round(patientResponsibility, 2),
            decimal.Round(insurerPayment, 2)
        );
    }
}
