using HealthPilot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Endpoints;

/// <summary>
/// Maps the <c>GET /health</c> and <c>GET /health/ready</c> endpoints used for
/// liveness and readiness probes (e.g., Kubernetes).
/// </summary>
public static class HealthEndpoints
{
    /// <summary>
    /// Registers the health check endpoints on the provided route builder.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithName("Health")
            .WithTags("Health")
            .WithOpenApi();

        endpoints.MapGet("/health/ready", HandleReadinessAsync)
            .WithName("Readiness")
            .WithTags("Health")
            .WithOpenApi();

        return endpoints;
    }

    private static async Task<IResult> HandleReadinessAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
        if (!canConnect)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
        var pendingCount = pendingMigrations.Count();

        return pendingCount > 0
            ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
            : Results.Ok(new { status = "ready" });
    }
}
