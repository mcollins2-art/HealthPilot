using HealthPilot.Api.Dtos;
using HealthPilot.Api.Middleware;
using HealthPilot.Api.Services;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HealthPilot.Api.Endpoints;

public static class EstimateEndpoints
{
    private static readonly Meter Meter = new("HealthPilot.Estimate");
    private static readonly Histogram<double> EstimateLatencyMs = Meter.CreateHistogram<double>("estimate_latency_ms");

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
        var started = Stopwatch.GetTimestamp();
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

        EstimateLatencyMs.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return Results.Ok(response);
    }
}
