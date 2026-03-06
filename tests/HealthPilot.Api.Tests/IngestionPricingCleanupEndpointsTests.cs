using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthPilot.Api.Data;
using HealthPilot.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HealthPilot.Api.Tests;

public class IngestionPricingCleanupEndpointsTests : IDisposable
{
    private readonly CleanupWebFactory _factory;
    private readonly HttpClient _client;

    public IngestionPricingCleanupEndpointsTests()
    {
        _factory = new CleanupWebFactory();
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-API-Key", "ingestion-key");
    }

    [Fact]
    public async Task PricingCleanup_ReturnsPreview_WhenDryRunDefaultsToTrue()
    {
        _factory.LifecycleService.StaleCounts = (7, 3);

        var response = await _client.PostAsync("/ingestion/pricing/cleanup?retentionDays=30", null);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.GetProperty("dryRun").GetBoolean());
        Assert.Equal(7, payload.GetProperty("negotiatedRatesCount").GetInt32());
        Assert.Equal(3, payload.GetProperty("cashPricesCount").GetInt32());
        Assert.Equal(0, _factory.LifecycleService.CleanupCallCount);
    }

    [Fact]
    public async Task PricingCleanup_ReturnsBadRequest_WhenConfirmMissing_ForDeletion()
    {
        var response = await _client.PostAsync("/ingestion/pricing/cleanup?retentionDays=30&dryRun=false", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Confirmation required", problem!.Title);
        Assert.Equal(0, _factory.LifecycleService.CleanupCallCount);
    }

    [Fact]
    public async Task PricingCleanup_Deletes_WhenDryRunDisabledAndConfirmTrue()
    {
        _factory.LifecycleService.StaleCounts = (5, 2);
        _factory.LifecycleService.DeleteResult = (4, 1);

        var response = await _client.PostAsync("/ingestion/pricing/cleanup?retentionDays=30&dryRun=false&confirm=true", null);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(payload.GetProperty("dryRun").GetBoolean());
        Assert.Equal(4, payload.GetProperty("negotiatedRatesDeleted").GetInt32());
        Assert.Equal(1, payload.GetProperty("cashPricesDeleted").GetInt32());
        Assert.Equal(5, payload.GetProperty("negotiatedRatesCount").GetInt32());
        Assert.Equal(2, payload.GetProperty("cashPricesCount").GetInt32());
        Assert.Equal(1, _factory.LifecycleService.CleanupCallCount);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private sealed class CleanupWebFactory : WebApplicationFactory<Program>
    {
        public FakePricingLifecycleService LifecycleService { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Security:ApiKeys:0:Name"] = "ingestion-client",
                    ["Security:ApiKeys:0:Key"] = "ingestion-key",
                    ["Security:ApiKeys:0:Scopes:0"] = "ingestion:write"
                });
            });

            builder.ConfigureWebHost(webBuilder =>
            {
                webBuilder.ConfigureTestServices(services =>
                {
                    var dbName = Guid.NewGuid().ToString("N");
                    services.RemoveAll<IPricingLifecycleService>();
                    services.RemoveAll<DbContextOptions<AppDbContext>>();
                    services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
                    services.AddSingleton<IPricingLifecycleService>(LifecycleService);
                });
            });

            return base.CreateHost(builder);
        }
    }

    private sealed class FakePricingLifecycleService : IPricingLifecycleService
    {
        public (int NegotiatedRatesCount, int CashPricesCount) StaleCounts { get; set; } = (0, 0);
        public (int NegotiatedRatesDeleted, int CashPricesDeleted) DeleteResult { get; set; } = (0, 0);
        public int CleanupCallCount { get; private set; }

        public Task<(int NegotiatedRatesCount, int CashPricesCount)> GetStalePricingCountsAsync(TimeSpan retention, CancellationToken cancellationToken)
        {
            return Task.FromResult(StaleCounts);
        }

        public Task<(int NegotiatedRatesDeleted, int CashPricesDeleted)> CleanupStalePricingAsync(TimeSpan retention, CancellationToken cancellationToken)
        {
            CleanupCallCount++;
            return Task.FromResult(DeleteResult);
        }
    }
}
