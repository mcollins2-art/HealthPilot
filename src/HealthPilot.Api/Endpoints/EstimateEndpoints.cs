using HealthPilot.Api.Dtos;
using HealthPilot.Api.Middleware;
using HealthPilot.Api.Services;

namespace HealthPilot.Api.Endpoints;

public static class EstimateEndpoints
{
    public static IEndpointRouteBuilder MapEstimateEndpoints(this IEndpointRouteBuilder endpoints, bool includeNames = true)
    {
        var estimateEndpoint = endpoints.MapPost("/estimate", HandleEstimateAsync)
            .RequireApiKeyScope("estimate:read")
            .RequireRateLimiting("api")
            .WithDtoValidation<EstimateRequest>()
            .WithTags("Estimate")
            .WithOpenApi();

        if (includeNames)
        {
            estimateEndpoint.WithName("EstimateOutOfPocket");
        }

        return endpoints;
    }

    private static async Task<IResult> HandleEstimateAsync(
        EstimateRequest request,
        IPricingQueryService pricingQueryService,
        IPricingSelectionStrategy pricingSelectionStrategy,
        IBenefitSimulationService benefitSimulationService,
        IEstimateAuditService estimateAuditService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Retrieve negotiated/cash pricing window for requested geography + CPT + insurer.
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

        await estimateAuditService.LogEstimateAsync(
            request,
            simulation,
            representativeRate,
            httpContext.TraceIdentifier,
            cancellationToken);

        var response = new EstimateResponse
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

        return Results.Ok(response);
    }
}
