using HealthPilot.Api.Dtos;

namespace HealthPilot.Api.Services;

public class OutpatientSurgeryEstimateService(
    IPricingQueryService pricingQueryService,
    IPricingSelectionStrategy pricingSelectionStrategy,
    IBenefitSimulationService benefitSimulationService) : IOutpatientSurgeryEstimateService
{
    public async Task<OutpatientSurgeryEstimateResponse> EstimateAsync(
        OutpatientSurgeryEstimateRequest request,
        CancellationToken cancellationToken)
    {
        PricingSummary pricing = await pricingQueryService.GetPricingSummaryAsync(
            request.ZipCode,
            request.Insurer,
            request.CptCode,
            cancellationToken);

        decimal representativeRate = pricingSelectionStrategy.SelectRepresentativeRate(pricing);

        BenefitSimulationResult simulation = benefitSimulationService.Simulate(new BenefitSimulationInput(
            representativeRate,
            request.DeductibleRemaining,
            request.CoinsurancePercent,
            request.Copay,
            request.OopMaxRemaining,
            request.CopayAppliesBeforeDeductible));

        return new OutpatientSurgeryEstimateResponse
        {
            NegotiatedRateMin = pricing.NegotiatedMin,
            NegotiatedRateMax = pricing.NegotiatedMax,
            NegotiatedRateRange = pricingQueryService.FormatRange(pricing.NegotiatedMin, pricing.NegotiatedMax),
            EstimatedOutOfPocket = simulation.EstimatedPatientResponsibility,
            CashPriceMin = pricing.CashMin,
            CashPriceMax = pricing.CashMax,
            CashPriceRange = pricingQueryService.FormatRange(pricing.CashMin, pricing.CashMax),
            InsurerPaymentEstimate = simulation.InsurerPayment,
            RoundingMode = MonetaryPolicy.RoundingMode.ToString()
        };
    }
}
