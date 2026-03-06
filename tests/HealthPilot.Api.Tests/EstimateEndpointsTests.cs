using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthPilot.Api.Dtos;
using HealthPilot.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HealthPilot.Api.Tests;

public class EstimateEndpointsTests
{
    [Fact]
    public async Task Estimate_ReturnsOk_WhenScopedKeyProvided()
    {
        using var factory = new EstimateWebFactory(allowScope: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "estimate-key");

        var request = new EstimateRequest
        {
            ZipCode = "10001",
            Insurer = "Aetna",
            CptCode = "70551",
            DeductibleRemaining = 1200,
            CoinsurancePercent = 20,
            Copay = 50,
            OopMaxRemaining = 3000,
            CopayAppliesBeforeDeductible = true
        };

        var response = await client.PostAsJsonAsync("/estimate", request);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(100m, payload.GetProperty("negotiatedRateMin").GetDecimal());
        Assert.Equal(200m, payload.GetProperty("negotiatedRateMax").GetDecimal());
        Assert.Equal("$100.00 - $200.00", payload.GetProperty("negotiatedRateRange").GetString());
        Assert.Equal(42.50m, payload.GetProperty("estimatedOutOfPocket").GetDecimal());
        Assert.Equal(80m, payload.GetProperty("cashPriceMin").GetDecimal());
        Assert.Equal(160m, payload.GetProperty("cashPriceMax").GetDecimal());
        Assert.Equal("$80.00 - $160.00", payload.GetProperty("cashPriceRange").GetString());
        Assert.Equal(57.50m, payload.GetProperty("insurerPaymentEstimate").GetDecimal());
        Assert.Equal("AwayFromZero", payload.GetProperty("roundingMode").GetString());
        Assert.Equal("1 facilities matched", payload.GetProperty("matchedFacilityCount").GetString());
        Assert.Equal("Aetna (Aetna-2026-Q1)", payload.GetProperty("matchedInsurer").GetString());
        Assert.Equal("2026-03-01T00:00:00+00:00", payload.GetProperty("dataAsOfDate").GetString());
    }

    [Fact]
    public async Task Estimate_ReturnsForbidden_WhenScopeMissing()
    {
        using var factory = new EstimateWebFactory(allowScope: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "estimate-key");

        var response = await client.PostAsJsonAsync("/estimate", new EstimateRequest
        {
            ZipCode = "10001",
            Insurer = "Aetna",
            CptCode = "70551",
            DeductibleRemaining = 1200,
            CoinsurancePercent = 20,
            Copay = 50,
            OopMaxRemaining = 3000,
            CopayAppliesBeforeDeductible = true
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed class EstimateWebFactory(bool allowScope) : WebApplicationFactory<Program>
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
                    ["Security:ApiKeys:0:Scopes:0"] = allowScope ? "estimate:read" : "ingestion:write",
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=healthpilot;Username=postgres;Password=postgres"
                });
            });

            builder.ConfigureWebHost(webBuilder =>
            {
                webBuilder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IPricingQueryService>();
                    services.RemoveAll<IBenefitSimulationService>();
                    services.RemoveAll<IEstimateAuditService>();

                    services.AddSingleton<IPricingQueryService>(new StubPricingQueryService());
                    services.AddSingleton<IBenefitSimulationService>(new StubBenefitSimulationService());
                    services.AddSingleton<IEstimateAuditService>(new StubEstimateAuditService());
                });
            });

            return base.CreateHost(builder);
        }
    }

    private sealed class StubPricingQueryService : IPricingQueryService
    {
        public Task<PricingSummary> GetPricingSummaryAsync(string zipCode, string insurer, string cptCode, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PricingSummary(
                100m,
                200m,
                80m,
                160m,
                MatchedFacilityCount: 1,
                MatchedInsurer: "Aetna (Aetna-2026-Q1)",
                DataAsOfDate: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)));
        }

        public string FormatRange(decimal? minValue, decimal? maxValue)
        {
            if (minValue is null || maxValue is null)
            {
                return "N/A";
            }

            return $"${minValue.Value:F2} - ${maxValue.Value:F2}";
        }
    }

    private sealed class StubBenefitSimulationService : IBenefitSimulationService
    {
        public BenefitSimulationResult Simulate(BenefitSimulationInput input)
        {
            return new BenefitSimulationResult(42.50m, 57.50m);
        }
    }

    private sealed class StubEstimateAuditService : IEstimateAuditService
    {
        public Task LogEstimateAsync(EstimateRequest request, BenefitSimulationResult result, decimal negotiatedRateUsed, string traceId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
