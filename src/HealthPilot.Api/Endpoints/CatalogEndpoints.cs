using HealthPilot.Api.Data;
using HealthPilot.Api.Middleware;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Endpoints;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/procedures", HandleProceduresAsync)
            .RequireApiKeyScope("estimate:read")
            .RequireRateLimiting("api")
            .WithName("GetProcedures")
            .WithTags("Catalog")
            .WithOpenApi();

        endpoints.MapGet("/providers", HandleProvidersAsync)
            .RequireApiKeyScope("estimate:read")
            .RequireRateLimiting("api")
            .WithName("GetProviders")
            .WithTags("Catalog")
            .WithOpenApi();

        return endpoints;
    }

    private static async Task<IResult> HandleProceduresAsync(
        AppDbContext dbContext,
        string? search,
        int? limit,
        CancellationToken cancellationToken)
    {
        var resolvedLimit = Math.Clamp(limit ?? 50, 1, 200);
        var query = dbContext.Procedures.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim();
            query = query.Where(p => p.CptCode.Contains(normalized) || p.Description.Contains(normalized));
        }

        var items = await query
            .OrderBy(p => p.CptCode)
            .Take(resolvedLimit)
            .Select(p => new { p.CptCode, p.Description, p.Category })
            .ToListAsync(cancellationToken);

        return Results.Ok(new { count = items.Count, items });
    }

    private static async Task<IResult> HandleProvidersAsync(
        AppDbContext dbContext,
        string? zipCode,
        string? state,
        int? limit,
        CancellationToken cancellationToken)
    {
        var resolvedLimit = Math.Clamp(limit ?? 50, 1, 200);
        var query = dbContext.Facilities.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(zipCode))
        {
            query = query.Where(f => f.Zip == zipCode.Trim());
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            query = query.Where(f => f.State == state.Trim().ToUpperInvariant());
        }

        var items = await query
            .OrderBy(f => f.Name)
            .ThenBy(f => f.Zip)
            .Take(resolvedLimit)
            .Select(f => new
            {
                providerName = f.Name,
                providerType = f.Type,
                f.City,
                f.State,
                zipCode = f.Zip
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(new { count = items.Count, items });
    }
}
