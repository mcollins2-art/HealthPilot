using HealthPilot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Endpoints;

public static class HealthEndpoints
{
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
        var queuedJobs = await dbContext.IngestionJobs.CountAsync(x => x.Status == "queued", cancellationToken);
        var inProgressJobs = await dbContext.IngestionJobs.CountAsync(x => x.Status == "in_progress", cancellationToken);

        return pendingCount > 0
            ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
            : Results.Ok(new { status = "ready", queuedJobs, inProgressJobs });
    }
}
