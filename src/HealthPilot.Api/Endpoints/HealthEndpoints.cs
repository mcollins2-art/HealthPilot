using HealthPilot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints, bool includeNames = true)
    {
        var healthEndpoint = endpoints.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithTags("Health")
            .WithOpenApi();

        var livenessEndpoint = endpoints.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
            .WithTags("Health")
            .WithOpenApi();

        var readinessEndpoint = endpoints.MapGet("/health/ready", HandleReadinessAsync)
            .WithTags("Health")
            .WithOpenApi();

        if (includeNames)
        {
            healthEndpoint.WithName("Health");
            livenessEndpoint.WithName("Liveness");
            readinessEndpoint.WithName("Readiness");
        }

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
