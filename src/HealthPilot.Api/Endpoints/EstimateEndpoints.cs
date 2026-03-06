using HealthPilot.Api.Dtos;
using HealthPilot.Api.Ingestion;
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
        IPricingSelectionStrategy pricingSelectionStrategy,
        IBenefitSimulationService benefitSimulationService,
        IEstimateAuditService estimateAuditService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var normalizedCpt = Normalizers.NormalizeCptCode(request.CptCode);
        if (!Normalizers.IsValidCptOrHcpcs(normalizedCpt))
        {
            return Results.BadRequest(new
            {
                error = "Invalid CPT/HCPCS code format. Expected 5 alphanumeric CPT or 4-2 HCPCS format.",
                traceId = httpContext.TraceIdentifier
            });
        }

        // Retrieve negotiated/cash pricing window for requested geography + CPT + insurer.
        PricingSummary pricing = await pricingQueryService.GetPricingSummaryAsync(
            request.ZipCode,
            request.Insurer,
            normalizedCpt,
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
            PricingLastUpdatedAt = pricing.PricingLastUpdatedAt,
            InsurerPaymentEstimate = simulation.InsurerPayment,
            RoundingMode = MonetaryPolicy.RoundingMode.ToString()
        };

        return Results.Ok(response);
    }
}
