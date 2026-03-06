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

public class OutpatientSurgeryEndpointsTests
{
    [Fact]
    public async Task Estimate_ReturnsOk_WhenScopedKeyProvided()
    {
        using var factory = new OutpatientSurgeryWebFactory(allowScope: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "outpatient-key");

        var request = new OutpatientSurgeryEstimateRequest
        {
            ZipCode = "10001",
            Insurer = "Aetna",
            CptCode = "47562",
            DeductibleRemaining = 1200,
            CoinsurancePercent = 20,
            Copay = 50,
            OopMaxRemaining = 3000,
            CopayAppliesBeforeDeductible = true
        };

        var response = await client.PostAsJsonAsync("/outpatient-surgery/estimate", request);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1500m, payload.GetProperty("negotiatedRateMin").GetDecimal());
        Assert.Equal(2400m, payload.GetProperty("negotiatedRateMax").GetDecimal());
        Assert.Equal("$1500.00 - $2400.00", payload.GetProperty("negotiatedRateRange").GetString());
        Assert.Equal(515m, payload.GetProperty("estimatedOutOfPocket").GetDecimal());
        Assert.Equal(1300m, payload.GetProperty("cashPriceMin").GetDecimal());
        Assert.Equal(2100m, payload.GetProperty("cashPriceMax").GetDecimal());
        Assert.Equal("$1300.00 - $2100.00", payload.GetProperty("cashPriceRange").GetString());
        Assert.Equal(1685m, payload.GetProperty("insurerPaymentEstimate").GetDecimal());
        Assert.Equal("AwayFromZero", payload.GetProperty("roundingMode").GetString());
    }

    [Fact]
    public async Task Estimate_ReturnsForbidden_WhenScopeMissing()
    {
        using var factory = new OutpatientSurgeryWebFactory(allowScope: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "outpatient-key");

        var response = await client.PostAsJsonAsync("/outpatient-surgery/estimate", new OutpatientSurgeryEstimateRequest
        {
            ZipCode = "10001",
            Insurer = "Aetna",
            CptCode = "47562",
            DeductibleRemaining = 1200,
            CoinsurancePercent = 20,
            Copay = 50,
            OopMaxRemaining = 3000,
            CopayAppliesBeforeDeductible = true
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed class OutpatientSurgeryWebFactory(bool allowScope) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Security:ApiKeys:0:Name"] = "outpatient-client",
                    ["Security:ApiKeys:0:Key"] = "outpatient-key",
                    ["Security:ApiKeys:0:Scopes:0"] = allowScope ? "outpatient-surgery:read" : "estimate:read",
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=healthpilot;Username=postgres;Password=postgres"
                });
            });

            builder.ConfigureWebHost(webBuilder =>
            {
                webBuilder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IOutpatientSurgeryEstimateService>();
                    services.AddSingleton<IOutpatientSurgeryEstimateService>(new StubOutpatientSurgeryEstimateService());
                });
            });

            return base.CreateHost(builder);
        }
    }

    private sealed class StubOutpatientSurgeryEstimateService : IOutpatientSurgeryEstimateService
    {
        public Task<OutpatientSurgeryEstimateResponse> EstimateAsync(OutpatientSurgeryEstimateRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new OutpatientSurgeryEstimateResponse
            {
                NegotiatedRateMin = 1500m,
                NegotiatedRateMax = 2400m,
                NegotiatedRateRange = "$1500.00 - $2400.00",
                EstimatedOutOfPocket = 515m,
                CashPriceMin = 1300m,
                CashPriceMax = 2100m,
                CashPriceRange = "$1300.00 - $2100.00",
                InsurerPaymentEstimate = 1685m,
                RoundingMode = "AwayFromZero"
            });
        }
    }
}
