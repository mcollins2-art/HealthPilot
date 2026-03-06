using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HealthPilot.Api.Tests;

public class ProgramStartupTests
{
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

    [Fact]
    public void Production_Starts_WhenLegacyApiKeyConfigured()
    {
        var allowedRoot = CreateTempDirectory();
        var previous = Environment.GetEnvironmentVariable("Security__ApiKey");
        var previousAllowedRoot = Environment.GetEnvironmentVariable("Ingestion__AllowedRootPath");
        try
        {
            Environment.SetEnvironmentVariable("Security__ApiKey", "legacy-key");
            Environment.SetEnvironmentVariable("Ingestion__AllowedRootPath", allowedRoot);

            using var factory = new StartupWebFactory(
                environmentName: Environments.Production,
                extraConfig: new Dictionary<string, string?>());

            using var client = factory.CreateClient();
            Assert.NotNull(client);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Security__ApiKey", previous);
            Environment.SetEnvironmentVariable("Ingestion__AllowedRootPath", previousAllowedRoot);
            Directory.Delete(allowedRoot, recursive: true);
        }
    }

    [Fact]
    public void Production_Starts_WhenScopedApiKeyConfigured()
    {
        var allowedRoot = CreateTempDirectory();
        var previousKey = Environment.GetEnvironmentVariable("Security__ApiKeys__0__Key");
        var previousName = Environment.GetEnvironmentVariable("Security__ApiKeys__0__Name");
        var previousAllowedRoot = Environment.GetEnvironmentVariable("Ingestion__AllowedRootPath");
        try
        {
            Environment.SetEnvironmentVariable("Security__ApiKeys__0__Name", "startup-test");
            Environment.SetEnvironmentVariable("Security__ApiKeys__0__Key", "scoped-key");
            Environment.SetEnvironmentVariable("Ingestion__AllowedRootPath", allowedRoot);

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
            Environment.SetEnvironmentVariable("Ingestion__AllowedRootPath", previousAllowedRoot);
            Directory.Delete(allowedRoot, recursive: true);
        }
    }

    [Fact]
    public void Production_Throws_WhenAllowedRootMissing()
    {
        var previous = Environment.GetEnvironmentVariable("Security__ApiKey");
        var previousAllowedRoot = Environment.GetEnvironmentVariable("Ingestion__AllowedRootPath");
        try
        {
            Environment.SetEnvironmentVariable("Security__ApiKey", "legacy-key");
            Environment.SetEnvironmentVariable("Ingestion__AllowedRootPath", "");

            using var factory = new StartupWebFactory(
                environmentName: Environments.Production,
                extraConfig: new Dictionary<string, string?>());

            var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
            Assert.Contains("Ingestion:AllowedRootPath must be configured", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Security__ApiKey", previous);
            Environment.SetEnvironmentVariable("Ingestion__AllowedRootPath", previousAllowedRoot);
        }
    }

    [Fact]
    public void Production_Throws_WhenAllowedRootDoesNotExist()
    {
        var previous = Environment.GetEnvironmentVariable("Security__ApiKey");
        var previousAllowedRoot = Environment.GetEnvironmentVariable("Ingestion__AllowedRootPath");
        try
        {
            Environment.SetEnvironmentVariable("Security__ApiKey", "legacy-key");

            var missingPath = Path.Combine(Path.GetTempPath(), "healthpilot-missing-ingestion-root", Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("Ingestion__AllowedRootPath", missingPath);
            using var factory = new StartupWebFactory(
                environmentName: Environments.Production,
                extraConfig: new Dictionary<string, string?>());

            var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
            Assert.Contains("must point to an existing directory", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Security__ApiKey", previous);
            Environment.SetEnvironmentVariable("Ingestion__AllowedRootPath", previousAllowedRoot);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "healthpilot-startup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
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
