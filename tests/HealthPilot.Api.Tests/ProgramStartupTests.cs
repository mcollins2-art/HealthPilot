using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HealthPilot.Api.Tests;

public class ProgramStartupTests
{
    private static readonly object EnvironmentLock = new();

    [Fact]
    public void Production_Throws_WhenNoApiKeysConfigured()
    {
        using var factory = new StartupWebFactory(
            environmentName: Environments.Production,
            extraConfig: new Dictionary<string, string?>
            {
                ["Security:ApiKey"] = "",
                ["Security:ApiKeys:0:Key"] = null
            });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Security:ApiKey or Security:ApiKeys must be configured", ex.Message);
    }

    [Theory]
    [InlineData("RateLimiting:PermitLimit", "0", "RateLimiting:PermitLimit must be greater than 0")]
    [InlineData("RateLimiting:WindowSeconds", "0", "RateLimiting:WindowSeconds must be greater than 0")]
    [InlineData("RateLimiting:QueueLimit", "-1", "RateLimiting:QueueLimit must be greater than or equal to 0")]
    public void Startup_Throws_WhenRateLimitConfigurationInvalid(string key, string value, string expectedMessage)
    {
        lock (EnvironmentLock)
        {
            var previous = Environment.GetEnvironmentVariable("Security__ApiKey");
            try
            {
                Environment.SetEnvironmentVariable("Security__ApiKey", "test-key");

                using var factory = new StartupWebFactory(
                    environmentName: Environments.Production,
                    extraConfig: new Dictionary<string, string?>
                    {
                        [key] = value
                    });

                var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
                Assert.Contains(expectedMessage, ex.ToString());
            }
            finally
            {
                Environment.SetEnvironmentVariable("Security__ApiKey", previous);
            }
        }
    }

    [Fact]
    public void Production_Starts_WhenLegacyApiKeyConfigured()
    {
        lock (EnvironmentLock)
        {
            var previous = Environment.GetEnvironmentVariable("Security__ApiKey");
            try
            {
                Environment.SetEnvironmentVariable("Security__ApiKey", "legacy-key");

                using var factory = new StartupWebFactory(
                    environmentName: Environments.Production,
                    extraConfig: new Dictionary<string, string?>());

                using var client = factory.CreateClient();
                Assert.NotNull(client);
            }
            finally
            {
                Environment.SetEnvironmentVariable("Security__ApiKey", previous);
            }
        }
    }

    [Fact]
    public void Production_Starts_WhenScopedApiKeyConfigured()
    {
        lock (EnvironmentLock)
        {
            var previousKey = Environment.GetEnvironmentVariable("Security__ApiKeys__0__Key");
            var previousName = Environment.GetEnvironmentVariable("Security__ApiKeys__0__Name");
            try
            {
                Environment.SetEnvironmentVariable("Security__ApiKeys__0__Name", "startup-test");
                Environment.SetEnvironmentVariable("Security__ApiKeys__0__Key", "scoped-key");

                using var factory = new StartupWebFactory(
                    environmentName: Environments.Production,
                    extraConfig: new Dictionary<string, string?>());

                using var client = factory.CreateClient();
                Assert.NotNull(client);
            }
            finally
            {
                Environment.SetEnvironmentVariable("Security__ApiKeys__0__Name", previousName);
                Environment.SetEnvironmentVariable("Security__ApiKeys__0__Key", previousKey);
            }
        }
    }

    private sealed class StartupWebFactory(string environmentName, Dictionary<string, string?> extraConfig) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment(environmentName);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=healthpilot;Username=postgres;Password=postgres"
                };

                foreach (var item in extraConfig)
                {
                    settings[item.Key] = item.Value;
                }

                config.AddInMemoryCollection(settings);
            });

            return base.CreateHost(builder);
        }
    }
}
