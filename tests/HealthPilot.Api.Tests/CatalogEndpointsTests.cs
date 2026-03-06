using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthPilot.Api.Data;
using HealthPilot.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HealthPilot.Api.Tests;

public class CatalogEndpointsTests
{
    [Fact]
    public async Task ProceduresEndpoint_ReturnsFilteredResults_WhenScopePresent()
    {
        await using var fixture = await CatalogWebFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "estimate-key");

        var response = await client.GetAsync("/procedures?search=705&limit=5");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, payload.GetProperty("count").GetInt32());
        Assert.Equal("70551", payload.GetProperty("items")[0].GetProperty("cptCode").GetString());
    }

    [Fact]
    public async Task ProvidersEndpoint_ReturnsZipFilteredResults_WhenScopePresent()
    {
        await using var fixture = await CatalogWebFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "estimate-key");

        var response = await client.GetAsync("/providers?zipCode=10001&limit=10");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, payload.GetProperty("count").GetInt32());
        Assert.Equal("Hospital A", payload.GetProperty("items")[0].GetProperty("providerName").GetString());
    }

    [Fact]
    public async Task CatalogEndpoints_ReturnUnauthorized_WhenApiKeyMissing()
    {
        await using var fixture = await CatalogWebFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/procedures");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class CatalogWebFixture : IAsyncDisposable
    {
        public required WebApplicationFactory<Program> Factory { get; init; }

        public static async Task<CatalogWebFixture> CreateAsync()
        {
            var fixture = new CatalogWebFixture
            {
                Factory = new CatalogWebFactory(Guid.NewGuid().ToString("N"))
            };
            await fixture.SeedAsync();
            return fixture;
        }

        private async Task SeedAsync()
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Procedures.AddRange(
                new Procedure { CptCode = "70551", Description = "Brain MRI", Category = "imaging" },
                new Procedure { CptCode = "80050", Description = "General health panel", Category = "lab" });

            db.Facilities.AddRange(
                new Facility { Name = "Hospital A", Type = "hospital", City = "New York", State = "NY", Zip = "10001" },
                new Facility { Name = "Hospital B", Type = "hospital", City = "Albany", State = "NY", Zip = "12207" });

            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Factory.Dispose();
            await Task.CompletedTask;
        }

        private sealed class CatalogWebFactory(string dbName) : WebApplicationFactory<Program>
        {
            protected override IHost CreateHost(IHostBuilder builder)
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Security:ApiKeys:0:Name"] = "estimate-client",
                        ["Security:ApiKeys:0:Key"] = "estimate-key",
                        ["Security:ApiKeys:0:Scopes:0"] = "estimate:read",
                        ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=healthpilot;Username=postgres;Password=postgres"
                    });
                });
                builder.ConfigureWebHost(webBuilder =>
                {
                    webBuilder.ConfigureTestServices(services =>
                    {
                        services.RemoveAll<DbContextOptions<AppDbContext>>();
                        services.AddDbContext<AppDbContext>(options =>
                            options.UseInMemoryDatabase(dbName));
                    });
                });

                return base.CreateHost(builder);
            }
        }
    }
}
