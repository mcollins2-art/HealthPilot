using HealthPilot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Tests;

/// <summary>
/// Minimal <see cref="IDbContextFactory{TContext}"/> used in integration-test
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> setups.
/// Replaces the production <see cref="IDbContextFactory{AppDbContext}"/> with one that
/// creates contexts against the test database (SQLite / in-memory).
/// </summary>
internal sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options)
    : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new AppDbContext(options));
}
