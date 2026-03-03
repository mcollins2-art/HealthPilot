using System.Text;
using HealthPilot.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HealthPilot.Api.Tests;

public class ApiKeyAuthenticationMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AllowsHealthPathWithoutHeader_WhenSecurityConfigured()
    {
        var middleware = CreateMiddleware(
            new Dictionary<string, string?>
            {
                ["Security:ApiKey"] = "configured-key"
            },
            isDevelopment: false,
            out var nextCalled);

        var context = CreateContext("/health");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
    }

    [Fact]
    public async Task InvokeAsync_AllowsSwaggerPathInDevelopment_WithoutHeader()
    {
        var middleware = CreateMiddleware(
            new Dictionary<string, string?>
            {
                ["Security:ApiKey"] = "configured-key"
            },
            isDevelopment: true,
            out var nextCalled);

        var context = CreateContext("/swagger/index.html");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
    }

    [Fact]
    public async Task InvokeAsync_AllowsRequest_WhenNoApiKeysConfigured()
    {
        var middleware = CreateMiddleware(
            new Dictionary<string, string?>(),
            isDevelopment: false,
            out var nextCalled);

        var context = CreateContext("/estimate");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
    }

    [Fact]
    public async Task InvokeAsync_ReturnsUnauthorized_WhenHeaderMissing()
    {
        var middleware = CreateMiddleware(
            new Dictionary<string, string?>
            {
                ["Security:ApiKey"] = "configured-key"
            },
            isDevelopment: false,
            out _);

        var context = CreateContext("/estimate");

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        var payload = await ReadBodyAsync(context);
        Assert.Contains("Missing required header", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsForbidden_WhenScopeRequirementNotMet()
    {
        var middleware = CreateMiddleware(
            new Dictionary<string, string?>
            {
                ["Security:ApiKeys:0:Name"] = "estimate-client",
                ["Security:ApiKeys:0:Key"] = "estimate-key",
                ["Security:ApiKeys:0:Scopes:0"] = "estimate:read"
            },
            isDevelopment: false,
            out _);

        var context = CreateContext("/ingestion/checkpoints");
        context.Request.Headers["X-API-Key"] = "estimate-key";
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new ApiKeyScopeRequirement("ingestion:write")),
            "scope-test"));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        var payload = await ReadBodyAsync(context);
        Assert.Contains("required scope", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_AllowsConfiguredScopedKey_AndSkipsBlankConfiguredKeys()
    {
        var middleware = CreateMiddleware(
            new Dictionary<string, string?>
            {
                ["Security:ApiKeys:0:Name"] = "blank-key",
                ["Security:ApiKeys:0:Key"] = "",
                ["Security:ApiKeys:0:Scopes:0"] = "ingestion:write",
                ["Security:ApiKeys:1:Name"] = "ingestion-client",
                ["Security:ApiKeys:1:Key"] = "ingestion-key",
                ["Security:ApiKeys:1:Scopes:0"] = "ingestion:write"
            },
            isDevelopment: false,
            out var nextCalled);

        var context = CreateContext("/ingestion/checkpoints");
        context.Request.Headers["X-API-Key"] = "ingestion-key";
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new ApiKeyScopeRequirement("ingestion:write")),
            "scope-test"));

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
    }

    [Fact]
    public async Task InvokeAsync_ReturnsUnauthorized_WhenProvidedKeyLengthDiffers()
    {
        var middleware = CreateMiddleware(
            new Dictionary<string, string?>
            {
                ["Security:ApiKey"] = "abcd1234"
            },
            isDevelopment: false,
            out _);

        var context = CreateContext("/estimate");
        context.Request.Headers["X-API-Key"] = "short";

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        var payload = await ReadBodyAsync(context);
        Assert.Contains("Invalid API key", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_AllowsRequest_WhenLegacyApiKeyMatches()
    {
        var middleware = CreateMiddleware(
            new Dictionary<string, string?>
            {
                ["Security:ApiKey"] = "legacy-key"
            },
            isDevelopment: false,
            out var nextCalled);

        var context = CreateContext("/estimate");
        context.Request.Headers["X-API-Key"] = "legacy-key";
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new ApiKeyScopeRequirement("estimate:read")),
            "legacy-scope-test"));

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
    }

    [Fact]
    public async Task InvokeAsync_AllowsRequest_WhenValidKeyAndNoScopeRequirement()
    {
        var middleware = CreateMiddleware(
            new Dictionary<string, string?>
            {
                ["Security:ApiKeys:0:Name"] = "estimate-client",
                ["Security:ApiKeys:0:Key"] = "estimate-key",
                ["Security:ApiKeys:0:Scopes:0"] = "estimate:read"
            },
            isDevelopment: false,
            out var nextCalled);

        var context = CreateContext("/estimate");
        context.Request.Headers["X-API-Key"] = "estimate-key";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
    }

    [Fact]
    public async Task InvokeAsync_ReturnsForbidden_WhenTenantRestrictedKeyMissingTenantHeader()
    {
        var middleware = CreateMiddleware(
            new Dictionary<string, string?>
            {
                ["Security:ApiKeys:0:Name"] = "tenant-client",
                ["Security:ApiKeys:0:Key"] = "tenant-key",
                ["Security:ApiKeys:0:Scopes:0"] = "estimate:read",
                ["Security:ApiKeys:0:Tenants:0"] = "tenant-a"
            },
            isDevelopment: false,
            out _);

        var context = CreateContext("/estimate");
        context.Request.Headers["X-API-Key"] = "tenant-key";

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        var payload = await ReadBodyAsync(context);
        Assert.Contains("tenant header", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_AllowsRequest_WhenTenantRestrictedKeyMatchesHeader()
    {
        var middleware = CreateMiddleware(
            new Dictionary<string, string?>
            {
                ["Security:ApiKeys:0:Name"] = "tenant-client",
                ["Security:ApiKeys:0:Key"] = "tenant-key",
                ["Security:ApiKeys:0:Scopes:0"] = "estimate:read",
                ["Security:ApiKeys:0:Tenants:0"] = "tenant-a"
            },
            isDevelopment: false,
            out var nextCalled);

        var context = CreateContext("/estimate");
        context.Request.Headers["X-API-Key"] = "tenant-key";
        context.Request.Headers["X-Tenant-Id"] = "tenant-a";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
        Assert.Equal("tenant-a", context.Items["TenantId"]);
    }

    private static ApiKeyAuthenticationMiddleware CreateMiddleware(
        IDictionary<string, string?> settings,
        bool isDevelopment,
        out Func<bool> nextCalled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var called = false;
        RequestDelegate next = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };

        nextCalled = () => called;

        return new ApiKeyAuthenticationMiddleware(
            next,
            configuration,
            new TestHostEnvironment(isDevelopment),
            NullLogger<ApiKeyAuthenticationMiddleware>.Instance);
    }

    private static DefaultHttpContext CreateContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private sealed class TestHostEnvironment(bool isDevelopment) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = isDevelopment ? Environments.Development : Environments.Production;
        public string ApplicationName { get; set; } = "HealthPilot.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
