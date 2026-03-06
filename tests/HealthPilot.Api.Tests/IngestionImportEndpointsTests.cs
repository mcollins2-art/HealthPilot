using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthPilot.Api.Data;
using HealthPilot.Api.Dtos;
using HealthPilot.Api.Ingestion;
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

public class IngestionImportEndpointsTests : IAsyncLifetime
{
    private const long NonExistentJobId = 999999;
    private const string TenantApiKey = "tenant-key";
    private string _tempDirectory = string.Empty;
    private ImportWebFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "healthpilot-import-endpoint-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _factory = CreateFactory();
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-API-Key", "ingestion-key");

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Import_ReturnsBadRequest_WhenFilePathMissing()
    {
        var response = await _client.PostAsJsonAsync("/ingestion/import", new IngestionImportRequest
        {
            FilePath = "",
            BatchSize = 100,
            ResumeFromCheckpoint = false
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Invalid request", problem!.Title);
    }

    [Fact]
    public async Task Import_ReturnsBadRequest_WhenOutsideAllowedRoot()
    {
        var allowedRoot = Path.Combine(_tempDirectory, "allowed");
        Directory.CreateDirectory(allowedRoot);

        var outsideFile = Path.Combine(_tempDirectory, "outside.csv");
        await File.WriteAllTextAsync(outsideFile, "cpt_code,description\n70551,Brain MRI");

        using var scopedFactory = CreateFactory(new Dictionary<string, string?>
        {
            ["Ingestion:AllowedRootPath"] = allowedRoot
        });
        using var scopedClient = scopedFactory.CreateClient();
        scopedClient.DefaultRequestHeaders.Add("X-API-Key", "ingestion-key");

        var response = await scopedClient.PostAsJsonAsync("/ingestion/import", new IngestionImportRequest
        {
            FilePath = outsideFile,
            BatchSize = 100,
            ResumeFromCheckpoint = false
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Invalid file path", problem!.Title);
    }

    [Fact]
    public async Task Import_ReturnsOk_WhenInsideAllowedRoot()
    {
        var allowedRoot = Path.Combine(_tempDirectory, "allowed-ok");
        Directory.CreateDirectory(allowedRoot);

        var insideFile = Path.Combine(allowedRoot, "inside.csv");
        await File.WriteAllTextAsync(insideFile,
            "cpt_code,description,category,facility_name,facility_type,city,state,zip,insurer,negotiated_rate,rate_type,cash_price\n" +
            "70551,Brain MRI,imaging,Test Hospital,hospital,Hoboken,NJ,07030,Plan A,1200,contracted,950");

        using var scopedFactory = CreateFactory(new Dictionary<string, string?>
        {
            ["Ingestion:AllowedRootPath"] = allowedRoot
        });
        using var scopedClient = scopedFactory.CreateClient();
        scopedClient.DefaultRequestHeaders.Add("X-API-Key", "ingestion-key");

        var response = await scopedClient.PostAsJsonAsync("/ingestion/import", new IngestionImportRequest
        {
            FilePath = insideFile,
            BatchSize = 100,
            ResumeFromCheckpoint = false
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("completed", payload.GetProperty("status").GetString());
        Assert.Equal(1, payload.GetProperty("recordsReceived").GetInt32());
    }

    [Fact]
    public async Task Import_ReturnsBadRequest_WhenFileDoesNotExist()
    {
        var missing = Path.Combine(_tempDirectory, "missing.csv");

        var response = await _client.PostAsJsonAsync("/ingestion/import", new IngestionImportRequest
        {
            FilePath = missing,
            BatchSize = 100,
            ResumeFromCheckpoint = false
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("File not found", problem!.Title);
    }

    [Fact]
    public async Task Import_ReturnsBadRequest_WhenFileTypeUnsupported()
    {
        var txt = Path.Combine(_tempDirectory, "unsupported.txt");
        await File.WriteAllTextAsync(txt, "hello");

        var response = await _client.PostAsJsonAsync("/ingestion/import", new IngestionImportRequest
        {
            FilePath = txt,
            BatchSize = 100,
            ResumeFromCheckpoint = false
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Unsupported file type", problem!.Title);
    }

    [Fact]
    public async Task Import_ReturnsBadRequest_WhenFileTooLarge()
    {
        var largeCsv = Path.Combine(_tempDirectory, "large.csv");
        await using (var stream = new FileStream(largeCsv, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(1_000_000_001);
        }

        var response = await _client.PostAsJsonAsync("/ingestion/import", new IngestionImportRequest
        {
            FilePath = largeCsv,
            BatchSize = 100,
            ResumeFromCheckpoint = false
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("File too large", problem!.Title);
    }

    [Fact]
    public async Task Import_ReturnsOk_WhenCsvProcessed()
    {
        var csv = Path.Combine(_tempDirectory, "small.csv");
        await File.WriteAllTextAsync(csv,
            "cpt_code,description,category,facility_name,facility_type,city,state,zip,insurer,negotiated_rate,rate_type,cash_price\n" +
            "70551,Brain MRI,imaging,Test Hospital,hospital,Hoboken,NJ,07030,Plan A,1200,contracted,950\n" +
            "70450,Head CT,imaging,Metro Clinic,clinic,Jersey City,NJ,07302,Plan B,900,contracted,700");

        var response = await _client.PostAsJsonAsync("/ingestion/import", new IngestionImportRequest
        {
            FilePath = csv,
            BatchSize = 1,
            ResumeFromCheckpoint = false
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("completed", payload.GetProperty("status").GetString());
        Assert.Equal(2, payload.GetProperty("recordsReceived").GetInt32());
        Assert.True(payload.GetProperty("completed").GetBoolean());
    }

    [Fact]
    public async Task Import_ReturnsAccepted_WhenAsyncRequested()
    {
        var csv = Path.Combine(_tempDirectory, "async.csv");
        await File.WriteAllTextAsync(csv,
            "cpt_code,description,category,facility_name,facility_type,city,state,zip,insurer,negotiated_rate,rate_type,cash_price\n" +
            "70551,Brain MRI,imaging,Test Hospital,hospital,Hoboken,NJ,07030,Plan A,1200,contracted,950");

        var response = await _client.PostAsJsonAsync("/ingestion/import", new IngestionImportRequest
        {
            FilePath = csv,
            BatchSize = 100,
            ResumeFromCheckpoint = false,
            Async = true
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("queued", payload.GetProperty("status").GetString());
        Assert.True(payload.GetProperty("jobId").GetInt64() > 0);
        Assert.Equal("cms_csv_v1", payload.GetProperty("parserVersion").GetString());
    }

    [Fact]
    public async Task Replay_ReturnsAccepted_ForExistingJob()
    {
        var csv = Path.Combine(_tempDirectory, "replay.csv");
        await File.WriteAllTextAsync(csv,
            "cpt_code,description,category,facility_name,facility_type,city,state,zip,insurer,negotiated_rate,rate_type,cash_price\n" +
            "70551,Brain MRI,imaging,Test Hospital,hospital,Hoboken,NJ,07030,Plan A,1200,contracted,950");

        var importResponse = await _client.PostAsJsonAsync("/ingestion/import", new IngestionImportRequest
        {
            FilePath = csv,
            BatchSize = 100,
            ResumeFromCheckpoint = false
        });

        importResponse.EnsureSuccessStatusCode();
        var importPayload = await importResponse.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = importPayload.GetProperty("jobId").GetInt64();

        var replayResponse = await _client.PostAsync($"/ingestion/jobs/{jobId}/replay", null);
        Assert.Equal(HttpStatusCode.Accepted, replayResponse.StatusCode);

        var replayPayload = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(jobId, replayPayload.GetProperty("replayOfJobId").GetInt64());
        Assert.True(replayPayload.GetProperty("jobId").GetInt64() > jobId);
    }

    [Fact]
    public async Task Replay_ReturnsNotFound_ForMissingJob()
    {
        var replayResponse = await _client.PostAsync($"/ingestion/jobs/{NonExistentJobId}/replay", null);

        Assert.Equal(HttpStatusCode.NotFound, replayResponse.StatusCode);
    }

    [Fact]
    public async Task JobStatus_ReturnsNotFound_WhenTenantDoesNotMatch()
    {
        using var tenantFactory = CreateTenantFactory();
        using var tenantClient = CreateTenantClient(tenantFactory, "tenant-a");
        var jobId = await CreateTenantJobAsync(tenantClient, "tenant-job-status.csv");

        tenantClient.DefaultRequestHeaders.Remove("X-Tenant-Id");
        tenantClient.DefaultRequestHeaders.Add("X-Tenant-Id", "tenant-b");

        var statusResponse = await tenantClient.GetAsync($"/ingestion/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.NotFound, statusResponse.StatusCode);
    }

    [Fact]
    public async Task Replay_ReturnsNotFound_WhenTenantDoesNotMatch()
    {
        using var tenantFactory = CreateTenantFactory();
        using var tenantClient = CreateTenantClient(tenantFactory, "tenant-a");
        var jobId = await CreateTenantJobAsync(tenantClient, "tenant-replay.csv");

        tenantClient.DefaultRequestHeaders.Remove("X-Tenant-Id");
        tenantClient.DefaultRequestHeaders.Add("X-Tenant-Id", "tenant-b");

        var replayResponse = await tenantClient.PostAsync($"/ingestion/jobs/{jobId}/replay", null);
        Assert.Equal(HttpStatusCode.NotFound, replayResponse.StatusCode);
    }

    [Fact]
    public async Task Import_ReturnsOk_WhenBatchSizeUsesConfiguredDefault()
    {
        var csv = Path.Combine(_tempDirectory, "default-batch.csv");
        await File.WriteAllTextAsync(csv,
            "cpt_code,description,category,facility_name,facility_type,city,state,zip,insurer,negotiated_rate,rate_type,cash_price\n" +
            "70551,Brain MRI,imaging,Test Hospital,hospital,Hoboken,NJ,07030,Plan A,1200,contracted,950");

        var response = await _client.PostAsJsonAsync("/ingestion/import", new IngestionImportRequest
        {
            FilePath = csv,
            BatchSize = null,
            ResumeFromCheckpoint = false
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("completed", payload.GetProperty("status").GetString());
        Assert.Equal(1, payload.GetProperty("recordsReceived").GetInt32());
    }

    [Fact]
    public async Task Import_ReturnsServerError_WhenPipelineThrows()
    {
        var csv = Path.Combine(_tempDirectory, "error.csv");
        await File.WriteAllTextAsync(csv,
            "cpt_code,description,category,facility_name,facility_type,city,state,zip,insurer,negotiated_rate,rate_type,cash_price\n" +
            "70551,Brain MRI,imaging,Test Hospital,hospital,Hoboken,NJ,07030,Plan A,1200,contracted,950");

        using var throwingFactory = CreateFactory(throwOnUpsert: true);
        using var throwingClient = throwingFactory.CreateClient();
        throwingClient.DefaultRequestHeaders.Add("X-API-Key", "ingestion-key");

        var response = await throwingClient.PostAsJsonAsync("/ingestion/import", new IngestionImportRequest
        {
            FilePath = csv,
            BatchSize = 10,
            ResumeFromCheckpoint = false
        });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Ingestion failed", problem!.Title);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();

        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private ImportWebFactory CreateFactory(Dictionary<string, string?>? extraConfig = null, bool throwOnUpsert = false)
    {
        return new ImportWebFactory(_tempDirectory, extraConfig, throwOnUpsert);
    }

    private ImportWebFactory CreateTenantFactory()
    {
        return CreateFactory(new Dictionary<string, string?>
        {
            ["Security:ApiKeys:0:Name"] = "tenant-client",
            ["Security:ApiKeys:0:Key"] = TenantApiKey,
            ["Security:ApiKeys:0:Scopes:0"] = "ingestion:write",
            ["Security:ApiKeys:0:Tenants:0"] = "tenant-a",
            ["Security:ApiKeys:0:Tenants:1"] = "tenant-b"
        });
    }

    private static HttpClient CreateTenantClient(WebApplicationFactory<Program> factory, string tenantId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", TenantApiKey);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
        return client;
    }

    private async Task<long> CreateTenantJobAsync(HttpClient client, string fileName)
    {
        var csv = Path.Combine(_tempDirectory, fileName);
        await File.WriteAllTextAsync(csv,
            "cpt_code,description,category,facility_name,facility_type,city,state,zip,insurer,negotiated_rate,rate_type,cash_price\n" +
            "70551,Brain MRI,imaging,Test Hospital,hospital,Hoboken,NJ,07030,Plan A,1200,contracted,950");

        var importResponse = await client.PostAsJsonAsync("/ingestion/import", new IngestionImportRequest
        {
            FilePath = csv,
            BatchSize = 100,
            ResumeFromCheckpoint = false
        });
        importResponse.EnsureSuccessStatusCode();

        var importPayload = await importResponse.Content.ReadFromJsonAsync<JsonElement>();
        return importPayload.GetProperty("jobId").GetInt64();
    }

    private sealed class ImportWebFactory(
        string tempDirectory,
        Dictionary<string, string?>? extraConfig = null,
        bool throwOnUpsert = false) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["Ingestion:CheckpointDirectory"] = Path.Combine(tempDirectory, "checkpoints"),
                    ["Ingestion:CheckpointRetentionHours"] = "168",
                    ["Ingestion:BatchSize"] = "5000",
                    ["Security:ApiKeys:0:Name"] = "ingestion-client",
                    ["Security:ApiKeys:0:Key"] = "ingestion-key",
                    ["Security:ApiKeys:0:Scopes:0"] = "ingestion:write",
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

            builder.ConfigureWebHost(webBuilder =>
            {
                webBuilder.ConfigureTestServices(services =>
                {
                    var dbName = Guid.NewGuid().ToString("N");
                    services.RemoveAll<IPricingPersistenceService>();
                    services.RemoveAll<IIngestionCheckpointService>();
                    services.RemoveAll<PricingIngestionPipeline>();
                    services.RemoveAll<DbContextOptions<AppDbContext>>();

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseInMemoryDatabase(dbName));

                    services.AddSingleton<IIngestionCheckpointService, TestCheckpointService>();
                    services.AddSingleton<IPricingPersistenceService>(_ => new TestPricingPersistenceService(throwOnUpsert));
                    services.AddScoped<PricingIngestionPipeline>();
                });
            });

            return base.CreateHost(builder);
        }
    }

    private sealed class TestPricingPersistenceService(bool throwOnUpsert) : IPricingPersistenceService
    {
        public Task<PricingPersistenceResult> UpsertPricingDataAsync(
            IReadOnlyList<StructuredPricingRecord> records,
            CancellationToken cancellationToken)
        {
            if (throwOnUpsert)
            {
                throw new InvalidOperationException("Forced persistence failure for tests.");
            }

            return Task.FromResult(new PricingPersistenceResult
            {
                RecordsReceived = records.Count,
                NegotiatedRatesUpserted = records.Count,
                CashPricesUpserted = records.Count
            });
        }
    }

    private sealed class TestCheckpointService : IIngestionCheckpointService
    {
        private readonly Dictionary<string, IngestionCheckpointRecord> _records = new(StringComparer.OrdinalIgnoreCase);

        public Task<IngestionCheckpointRecord> GetOrCreateAsync(string filePath, int batchSize, CancellationToken cancellationToken)
        {
            var fullPath = Path.GetFullPath(filePath);
            var existing = _records.Values.FirstOrDefault(x => string.Equals(x.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            var checkpoint = new IngestionCheckpointRecord
            {
                CheckpointKey = Guid.NewGuid().ToString("N"),
                FilePath = fullPath,
                BatchSize = batchSize,
                RowsProcessed = 0,
                Status = "in_progress",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            _records[checkpoint.CheckpointKey] = checkpoint;
            return Task.FromResult(checkpoint);
        }

        public Task<IngestionCheckpointRecord?> GetByKeyAsync(string checkpointKey, CancellationToken cancellationToken)
        {
            _records.TryGetValue(checkpointKey, out var value);
            return Task.FromResult(value);
        }

        public Task<IReadOnlyList<IngestionCheckpointRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken)
        {
            IReadOnlyList<IngestionCheckpointRecord> items = _records.Values
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Take(limit)
                .ToList();

            return Task.FromResult(items);
        }

        public Task<int> CleanupExpiredAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        public Task<int> CleanupExpiredAsync(TimeSpan retention, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        public Task SaveProgressAsync(string checkpointKey, int rowsProcessed, CancellationToken cancellationToken)
        {
            if (!_records.TryGetValue(checkpointKey, out var checkpoint))
            {
                throw new InvalidOperationException("Checkpoint not found");
            }

            checkpoint.RowsProcessed = rowsProcessed;
            checkpoint.Status = "in_progress";
            checkpoint.UpdatedAtUtc = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }

        public Task MarkCompletedAsync(string checkpointKey, int rowsProcessed, CancellationToken cancellationToken)
        {
            if (!_records.TryGetValue(checkpointKey, out var checkpoint))
            {
                throw new InvalidOperationException("Checkpoint not found");
            }

            checkpoint.RowsProcessed = rowsProcessed;
            checkpoint.Status = "completed";
            checkpoint.UpdatedAtUtc = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }
    }
}
