using HealthPilot.Api.Dtos;
using HealthPilot.Api.Middleware;
using HealthPilot.Api.Services;

namespace HealthPilot.Api.Endpoints;

public static class OutpatientSurgeryEndpoints
{
    public static IEndpointRouteBuilder MapOutpatientSurgeryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/outpatient-surgery/estimate", HandleEstimateAsync)
            .RequireApiKeyScope("outpatient-surgery:read")
            .RequireRateLimiting("api")
            .WithName("EstimateOutpatientSurgery")
            .WithTags("OutpatientSurgery")
            .WithOpenApi();

        return endpoints;
    }

    private static async Task<IResult> HandleEstimateAsync(
        OutpatientSurgeryEstimateRequest request,
        IOutpatientSurgeryEstimateService outpatientSurgeryEstimateService,
        CancellationToken cancellationToken)
    {
        OutpatientSurgeryEstimateResponse response = await outpatientSurgeryEstimateService.EstimateAsync(request, cancellationToken);
        return Results.Ok(response);
    }
}
