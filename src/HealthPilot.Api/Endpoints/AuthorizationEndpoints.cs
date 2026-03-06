using HealthPilot.Api.Dtos;
using HealthPilot.Api.Middleware;
using HealthPilot.Api.Services;

namespace HealthPilot.Api.Endpoints;

public static class AuthorizationEndpoints
{
    public static IEndpointRouteBuilder MapAuthorizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/authorization-estimate", HandleAuthorizationEstimateAsync)
            .RequireApiKeyScope("authorization:read")
            .RequireRateLimiting("api")
            .WithName("EstimateAuthorization")
            .WithTags("Authorization")
            .WithOpenApi();

        return endpoints;
    }

    private static async Task<IResult> HandleAuthorizationEstimateAsync(
        AuthorizationEstimateRequest request,
        IAuthorizationDecisionService authorizationDecisionService,
        CancellationToken cancellationToken)
    {
        var response = await authorizationDecisionService.EstimateAsync(request, cancellationToken);
        return Results.Ok(response);
    }
}
