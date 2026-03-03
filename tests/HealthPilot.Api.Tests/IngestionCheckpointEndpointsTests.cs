using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using HealthPilot.Api.Ingestion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HealthPilot.Api.Tests;

public class IngestionCheckpointEndpointsTests : IAsyncLifetime
{
    private string _checkpointDirectory = string.Empty;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _checkpointDirectory = Path.Combine(Path.GetTempPath(), "healthpilot-checkpoint-endpoint-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_checkpointDirectory);

        _factory = new CheckpointWebFactory(_checkpointDirectory);
        _client = _factory.CreateClient();

        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetCheckpointByKey_ReturnsCheckpointRecord()
    {
        var key = await CreateCheckpointAsync("C:\\data\\endpoint-one.json", 1500, 123, completed: false);

        var response = await _client.GetAsync($"/ingestion/checkpoints/{key}");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(key, payload.GetProperty("checkpointKey").GetString());
        Assert.Equal(123, payload.GetProperty("rowsProcessed").GetInt32());
        Assert.Equal("in_progress", payload.GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetCheckpointByKey_ReturnsNotFound_WhenMissing()
    {
        var response = await _client.GetAsync("/ingestion/checkpoints/missing-key");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(payload);
        Assert.Equal("Checkpoint not found", payload!.Title);
    }

    [Fact]
    public async Task GetCheckpointByKey_ReturnsBadRequest_WhenKeyIsWhitespace()
    {
        var response = await _client.GetAsync("/ingestion/checkpoints/%20");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(payload);
        Assert.Equal("Invalid request", payload!.Title);
    }

    [Fact]
    public async Task ListCheckpoints_ReturnsCountAndItems_WithLimitApplied()
    {
        await CreateCheckpointAsync("C:\\data\\list-a.json", 1000, 10, completed: false);
        await Task.Delay(20);
        var keyB = await CreateCheckpointAsync("C:\\data\\list-b.json", 1000, 20, completed: true);
        await Task.Delay(20);
        var keyC = await CreateCheckpointAsync("C:\\data\\list-c.json", 1000, 30, completed: true);

        var response = await _client.GetAsync("/ingestion/checkpoints?limit=2");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, payload.GetProperty("count").GetInt32());
        var items = payload.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(keyC, items[0].GetProperty("checkpointKey").GetString());
        Assert.Equal(keyB, items[1].GetProperty("checkpointKey").GetString());
    }

    [Fact]
    public async Task CleanupCheckpoints_RemovesExpiredFiles_WhenRetentionProvided()
    {
        var oldKey = await CreateCheckpointAsync("C:\\data\\cleanup-old.json", 1000, 5, completed: true);
        var freshKey = await CreateCheckpointAsync("C:\\data\\cleanup-fresh.json", 1000, 15, completed: true);

        var oldFile = Path.Combine(_checkpointDirectory, oldKey + ".json");
        var freshFile = Path.Combine(_checkpointDirectory, freshKey + ".json");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddHours(-3));
        File.SetLastWriteTimeUtc(freshFile, DateTime.UtcNow.AddMinutes(-1));

        var cleanupResponse = await _client.PostAsync("/ingestion/checkpoints/cleanup?retentionHours=1", content: null);

        cleanupResponse.EnsureSuccessStatusCode();
        var cleanupPayload = await cleanupResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, cleanupPayload.GetProperty("deleted").GetInt32());

        var listResponse = await _client.GetAsync("/ingestion/checkpoints?limit=10");
        listResponse.EnsureSuccessStatusCode();
        var listPayload = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var keys = listPayload.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("checkpointKey").GetString()).ToList();

        Assert.DoesNotContain(oldKey, keys);
        Assert.Contains(freshKey, keys);
    }

    [Fact]
    public async Task CleanupCheckpoints_ReturnsBadRequest_WhenRetentionInvalid()
    {
        var response = await _client.PostAsync("/ingestion/checkpoints/cleanup?retentionHours=0", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(payload);
        Assert.Equal("Invalid request", payload!.Title);
    }

    [Fact]
    public async Task CleanupCheckpoints_UsesConfiguredRetention_WhenRetentionMissing()
    {
        var response = await _client.PostAsync("/ingestion/checkpoints/cleanup", content: null);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(168, payload.GetProperty("retentionHours").GetInt32());
    }

    [Fact]
    public async Task CheckpointEndpoints_ReturnUnauthorized_WhenApiKeyMissing_AndSecurityConfigured()
    {
        using var secureFactory = CreateSecureFactory("valid-ingestion-key", new[] { "ingestion:write" });
        using var secureClient = secureFactory.CreateClient();

        var response = await secureClient.GetAsync("/ingestion/checkpoints?limit=5");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CheckpointEndpoints_ReturnUnauthorized_WhenApiKeyInvalid_AndSecurityConfigured()
    {
        using var secureFactory = CreateSecureFactory("valid-ingestion-key", new[] { "ingestion:write" });
        using var secureClient = secureFactory.CreateClient();
        secureClient.DefaultRequestHeaders.Add("X-API-Key", "wrong-key");

        var response = await secureClient.GetAsync("/ingestion/checkpoints?limit=5");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CheckpointEndpoints_ReturnForbidden_WhenScopeMissing()
    {
        using var secureFactory = CreateSecureFactory("estimate-only-key", new[] { "estimate:read" });
        using var secureClient = secureFactory.CreateClient();
        secureClient.DefaultRequestHeaders.Add("X-API-Key", "estimate-only-key");

        var response = await secureClient.GetAsync("/ingestion/checkpoints?limit=5");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CheckpointEndpoints_ReturnOk_WhenScopePresent()
    {
        using var secureFactory = CreateSecureFactory("ingestion-key", new[] { "ingestion:write" });
        using var secureClient = secureFactory.CreateClient();
        secureClient.DefaultRequestHeaders.Add("X-API-Key", "ingestion-key");

        var response = await secureClient.GetAsync("/ingestion/checkpoints?limit=5");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.TryGetProperty("count", out _));
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();

        if (Directory.Exists(_checkpointDirectory))
        {
            Directory.Delete(_checkpointDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private async Task<string> CreateCheckpointAsync(string filePath, int batchSize, int rowsProcessed, bool completed)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IIngestionCheckpointService>();

        var checkpoint = await service.GetOrCreateAsync(filePath, batchSize, CancellationToken.None);
        if (completed)
        {
            await service.MarkCompletedAsync(checkpoint.CheckpointKey, rowsProcessed, CancellationToken.None);
        }
        else
        {
            await service.SaveProgressAsync(checkpoint.CheckpointKey, rowsProcessed, CancellationToken.None);
        }

        return checkpoint.CheckpointKey;
    }

    private CheckpointWebFactory CreateSecureFactory(string key, string[] scopes)
    {
        return new CheckpointWebFactory(
            _checkpointDirectory,
            new Dictionary<string, string?>
            {
                ["Security:ApiKeys:0:Name"] = "test-client",
                ["Security:ApiKeys:0:Key"] = key,
                ["Security:ApiKeys:0:Scopes:0"] = scopes.ElementAtOrDefault(0),
                ["Security:ApiKeys:0:Scopes:1"] = scopes.ElementAtOrDefault(1),
                ["Security:ApiKeys:0:Scopes:2"] = scopes.ElementAtOrDefault(2)
            });
    }

    private sealed class CheckpointWebFactory(string checkpointDirectory, Dictionary<string, string?>? extraConfig = null) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["Ingestion:CheckpointDirectory"] = checkpointDirectory,
                    ["Ingestion:CheckpointRetentionHours"] = "168",
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=healthpilot;Username=postgres;Password=postgres"
                };

                if (extraConfig is not null)
                {
                    foreach (var entry in extraConfig)
                    {
                        settings[entry.Key] = entry.Value;
                    }
                }

                config.AddInMemoryCollection(settings);
            });

            return base.CreateHost(builder);
        }
    }
}
