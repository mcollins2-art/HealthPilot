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

public class AuthorizationEndpointsTests
{
    [Fact]
    public async Task AuthorizationEstimate_ReturnsOk_WhenScopedKeyProvided()
    {
        using var factory = new AuthorizationWebFactory(allowScope: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "auth-key");

        var request = new AuthorizationEstimateRequest
        {
            Insurer = "Aetna",
            ProcedureCpt = "72141",
            DiagnosisIcd10 = "M54.2",
            Age = 45
        };

        var response = await client.PostAsJsonAsync("/authorization-estimate", request);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.GetProperty("authorizationRequired").GetBoolean());
        Assert.Equal(0.58m, payload.GetProperty("approvalProbability").GetDecimal());
        Assert.Equal("No documented conservative treatment", payload.GetProperty("commonDenialReason").GetString());
        Assert.Equal(2, payload.GetProperty("requiredConditions").GetArrayLength());
    }

    [Fact]
    public async Task AuthorizationEstimate_ReturnsForbidden_WhenScopeMissing()
    {
        using var factory = new AuthorizationWebFactory(allowScope: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "auth-key");

        var response = await client.PostAsJsonAsync("/authorization-estimate", new AuthorizationEstimateRequest
        {
            Insurer = "Aetna",
            ProcedureCpt = "72141",
            DiagnosisIcd10 = "M54.2",
            Age = 45
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed class AuthorizationWebFactory(bool allowScope) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Security:ApiKeys:0:Name"] = "authorization-client",
                    ["Security:ApiKeys:0:Key"] = "auth-key",
                    ["Security:ApiKeys:0:Scopes:0"] = allowScope ? "authorization:read" : "estimate:read",
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=healthpilot;Username=postgres;Password=postgres"
                });
            });

            builder.ConfigureWebHost(webBuilder =>
            {
                webBuilder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IAuthorizationDecisionService>();
                    services.AddSingleton<IAuthorizationDecisionService>(new StubAuthorizationDecisionService());
                });
            });

            return base.CreateHost(builder);
        }
    }

    private sealed class StubAuthorizationDecisionService : IAuthorizationDecisionService
    {
        public Task<AuthorizationEstimateResponse> EstimateAsync(AuthorizationEstimateRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AuthorizationEstimateResponse
            {
                AuthorizationRequired = true,
                ApprovalProbability = 0.58m,
                RequiredConditions = ["6 weeks conservative therapy", "neurological deficit documentation"],
                CommonDenialReason = "No documented conservative treatment"
            });
        }
    }
}
