using HealthPilot.Api.Dtos;
using HealthPilot.Api.Middleware;
using HealthPilot.Api.Services;

namespace HealthPilot.Api.Endpoints;

public static class EstimateEndpoints
{
    public static IEndpointRouteBuilder MapEstimateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/estimate", HandleEstimateAsync)
            .RequireApiKeyScope("estimate:read")
            .RequireRateLimiting("api")
            .WithName("EstimateOutOfPocket")
            .WithTags("Estimate")
            .WithOpenApi();

        return endpoints;
    }

    private static async Task<IResult> HandleEstimateAsync(
        EstimateRequest request,
        IPricingQueryService pricingQueryService,
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

        // Phase 1 uses the lower end of negotiated rates as a conservative reference.
        decimal representativeRate = pricing.NegotiatedMin ?? 0m;

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
            NegotiatedRateRange = pricingQueryService.FormatRange(pricing.NegotiatedMin, pricing.NegotiatedMax),
            EstimatedOutOfPocket = $"${simulation.EstimatedPatientResponsibility:F2}",
            CashPriceRange = pricingQueryService.FormatRange(pricing.CashMin, pricing.CashMax),
            InsurerPaymentEstimate = $"${simulation.InsurerPayment:F2}"
        };

        return Results.Ok(response);
    }
}
