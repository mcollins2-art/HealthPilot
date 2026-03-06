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
        endpoints.MapPost("/estimate-cost", HandleEstimateAsync)
            .RequireApiKeyScope("estimate:read")
            .RequireRateLimiting("api")
            .WithName("EstimateProcedureCost")
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

        var cheapestProvider = await pricingQueryService.GetCheapestProviderAsync(
            request.ZipCode,
            request.Insurer,
            request.CptCode,
            cancellationToken);

        var expectedCostMin = pricing.NegotiatedMin ?? pricing.CashMin;
        var expectedCostMax = pricing.NegotiatedMax ?? pricing.CashMax;

        var response = new EstimateResponse
        {
            NegotiatedRateMin = pricing.NegotiatedMin,
            NegotiatedRateMax = pricing.NegotiatedMax,
            NegotiatedRateRange = pricingQueryService.FormatRange(pricing.NegotiatedMin, pricing.NegotiatedMax),
            EstimatedOutOfPocket = simulation.EstimatedPatientResponsibility,
            CashPriceMin = pricing.CashMin,
            CashPriceMax = pricing.CashMax,
            CashPriceRange = pricingQueryService.FormatRange(pricing.CashMin, pricing.CashMax),
            ExpectedCostRange = pricingQueryService.FormatRange(expectedCostMin, expectedCostMax),
            ConfidenceScore = CalculateConfidenceScore(pricing),
            CheapestNearbyProvider = cheapestProvider is null
                ? null
                : new CheapestProviderResponse
                {
                    ProviderName = cheapestProvider.ProviderName,
                    City = cheapestProvider.City,
                    State = cheapestProvider.State,
                    ZipCode = cheapestProvider.ZipCode,
                    NegotiatedRate = cheapestProvider.NegotiatedRate,
                    CashPrice = cheapestProvider.CashPrice,
                    SelectedPrice = cheapestProvider.SelectedPrice
                },
            InsurerPaymentEstimate = simulation.InsurerPayment,
            RoundingMode = MonetaryPolicy.RoundingMode.ToString()
        };

        return Results.Ok(response);
    }

    private static decimal CalculateConfidenceScore(PricingSummary pricing)
    {
        var hasNegotiated = pricing.NegotiatedMin.HasValue && pricing.NegotiatedMax.HasValue;
        var hasCash = pricing.CashMin.HasValue && pricing.CashMax.HasValue;

        return (hasNegotiated, hasCash) switch
        {
            (true, true) => 0.92m,
            (true, false) => 0.85m,
            (false, true) => 0.70m,
            _ => 0.25m
        };
    }
}
