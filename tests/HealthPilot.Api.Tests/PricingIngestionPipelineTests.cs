using HealthPilot.Api.Data;
using HealthPilot.Api.Dtos;
using HealthPilot.Api.Ingestion;
using HealthPilot.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthPilot.Api.Tests;

public class PricingIngestionPipelineTests : IDisposable
{
    private readonly string _tempDirectory;

    public PricingIngestionPipelineTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "healthpilot-pipeline-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task ImportFileWithBatchingAsync_Throws_WhenBatchSizeInvalid()
    {
        using var dbContext = CreateDbContext();
        var persistence = new FakePricingPersistenceService();
        var checkpoints = new FakeCheckpointService();
        var pipeline = new PricingIngestionPipeline(dbContext, persistence, checkpoints);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            pipeline.ImportFileWithBatchingAsync("input.csv", 0, resumeFromCheckpoint: false, CancellationToken.None));
    }

    [Fact]
    public async Task ImportFileWithBatchingAsync_Throws_WhenExtensionUnsupported()
    {
        using var dbContext = CreateDbContext();
        var persistence = new FakePricingPersistenceService();
        var checkpoints = new FakeCheckpointService();
        var pipeline = new PricingIngestionPipeline(dbContext, persistence, checkpoints);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            pipeline.ImportFileWithBatchingAsync("input.txt", 100, resumeFromCheckpoint: false, CancellationToken.None));
    }

    [Fact]
    public async Task ParseAsync_ReturnsRecords_ForCsvAndJson()
    {
        var csvPath = Path.Combine(_tempDirectory, "parse.csv");
        File.WriteAllText(csvPath,
            "cpt_code,description,category,facility_name,facility_type,city,state,zip,insurer,negotiated_rate,rate_type,cash_price\n" +
            "70551,Brain MRI,imaging,Test Hospital,hospital,Hoboken,NJ,07030,Plan A,1200,contracted,950");

        var jsonPath = Path.Combine(_tempDirectory, "parse.json");
        await File.WriteAllTextAsync(jsonPath, """
        [
          {
            "cpt_code": "70450",
            "description": "Head CT",
            "category": "imaging",
            "facility_name": "Metro Clinic",
            "facility_type": "clinic",
            "city": "Jersey City",
            "state": "NJ",
            "zip": "07302",
            "insurer": "Plan B",
            "negotiated_rate": 900,
            "rate_type": "contracted",
            "cash_price": 700
          }
        ]
        """);

        using var dbContext = CreateDbContext();
        var pipeline = new PricingIngestionPipeline(dbContext, new FakePricingPersistenceService(), new FakeCheckpointService());

        var csvRecords = await pipeline.ParseAsync(csvPath, CancellationToken.None);
        var jsonRecords = await pipeline.ParseAsync(jsonPath, CancellationToken.None);

        Assert.Single(csvRecords);
        Assert.Equal("70551", csvRecords[0].CptCode);
        Assert.Single(jsonRecords);
        Assert.Equal("70450", jsonRecords[0].CptCode);
    }

    [Fact]
    public async Task ParseAsync_Throws_WhenExtensionUnsupported()
    {
        var filePath = Path.Combine(_tempDirectory, "parse.unsupported");
        await File.WriteAllTextAsync(filePath, "irrelevant");

        using var dbContext = CreateDbContext();
        var pipeline = new PricingIngestionPipeline(dbContext, new FakePricingPersistenceService(), new FakeCheckpointService());

        await Assert.ThrowsAsync<NotSupportedException>(() => pipeline.ParseAsync(filePath, CancellationToken.None));
    }

    [Fact]
    public async Task ImportFileWithBatchingAsync_Csv_ProcessesInBatchesAndCompletesCheckpoint()
    {
        var filePath = Path.Combine(_tempDirectory, "rates.csv");
        File.WriteAllText(filePath,
            "cpt_code,description,category,facility_name,facility_type,city,state,zip,insurer,negotiated_rate,rate_type,cash_price\n" +
            "70551,Brain MRI,imaging,Test Hospital,hospital,Hoboken,NJ,07030,Plan A,1200,contracted,950\n" +
            ",Missing CPT,imaging,Test Hospital,hospital,Hoboken,NJ,07030,Plan A,1300,contracted,980\n" +
            "70450,Head CT,imaging,Metro Clinic,clinic,Jersey City,NJ,07302,Plan B,900,contracted,700");

        using var dbContext = CreateDbContext();
        var persistence = new FakePricingPersistenceService();
        var checkpoints = new FakeCheckpointService();
        var pipeline = new PricingIngestionPipeline(dbContext, persistence, checkpoints);

        var result = await pipeline.ImportFileWithBatchingAsync(filePath, 1, resumeFromCheckpoint: false, CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(3, result.RowsProcessed);
        Assert.Equal(0, result.RowsResumedFrom);
        Assert.NotEmpty(result.CheckpointKey);

        Assert.Equal(2, persistence.Calls.Count);
        Assert.Equal(2, result.Persistence.RecordsReceived);

        Assert.NotNull(checkpoints.Completed);
        Assert.Equal(3, checkpoints.Completed!.RowsProcessed);
        Assert.Contains(checkpoints.SavedProgress, rows => rows == 1);
        Assert.Contains(checkpoints.SavedProgress, rows => rows == 3);
    }

    [Fact]
    public async Task ImportFileWithBatchingAsync_Json_ResumesFromCheckpointOffset()
    {
        var filePath = Path.Combine(_tempDirectory, "rates.json");
        await File.WriteAllTextAsync(filePath, """
        [
          {
            "cpt_code": "70551",
            "description": "Brain MRI",
            "category": "imaging",
            "facility_name": "Hospital A",
            "facility_type": "hospital",
            "city": "Hoboken",
            "state": "NJ",
            "zip": "07030",
            "insurer": "Plan A",
            "negotiated_rate": 1200,
            "rate_type": "contracted",
            "cash_price": 950
          },
          {
            "cpt_code": "70450",
            "description": "Head CT",
            "category": "imaging",
            "facility_name": "Hospital B",
            "facility_type": "hospital",
            "city": "Jersey City",
            "state": "NJ",
            "zip": "07302",
            "insurer": "Plan B",
            "negotiated_rate": 900,
            "rate_type": "contracted",
            "cash_price": 700
          }
        ]
        """);

        using var dbContext = CreateDbContext();
        var persistence = new FakePricingPersistenceService();
        var checkpoints = new FakeCheckpointService(initialRowsProcessed: 1);
        var pipeline = new PricingIngestionPipeline(dbContext, persistence, checkpoints);

        var result = await pipeline.ImportFileWithBatchingAsync(filePath, 50, resumeFromCheckpoint: true, CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(1, result.RowsResumedFrom);
        Assert.Equal(2, result.RowsProcessed);
        Assert.Single(persistence.Calls);
        Assert.Single(persistence.Calls[0]);
        Assert.Equal("70450", persistence.Calls[0][0].CptCode);
        Assert.Equal(1, result.Persistence.RecordsReceived);
    }

        [Fact]
        public async Task ImportFileWithBatchingAsync_Csv_ResumesFromOffset_WhenResumeEnabled()
        {
                var filePath = Path.Combine(_tempDirectory, "resume.csv");
                File.WriteAllText(filePath,
                        "cpt_code,description,category,facility_name,facility_type,city,state,zip,insurer,negotiated_rate,rate_type,cash_price\n" +
                        "70551,Brain MRI,imaging,Test Hospital,hospital,Hoboken,NJ,07030,Plan A,1200,contracted,950\n" +
                        "70450,Head CT,imaging,Metro Clinic,clinic,Jersey City,NJ,07302,Plan B,900,contracted,700\n" +
                        "71260,Chest CT,imaging,Metro Clinic,clinic,Jersey City,NJ,07302,Plan C,1100,contracted,800");

                using var dbContext = CreateDbContext();
                var persistence = new FakePricingPersistenceService();
                var checkpoints = new FakeCheckpointService(initialRowsProcessed: 2);
                var pipeline = new PricingIngestionPipeline(dbContext, persistence, checkpoints);

                var result = await pipeline.ImportFileWithBatchingAsync(filePath, 10, resumeFromCheckpoint: true, CancellationToken.None);

                Assert.True(result.Completed);
                Assert.Equal(2, result.RowsResumedFrom);
                Assert.Equal(3, result.RowsProcessed);
                Assert.Single(persistence.Calls);
                Assert.Single(persistence.Calls[0]);
                Assert.Equal("71260", persistence.Calls[0][0].CptCode);
        }

        [Fact]
        public async Task ImportFileWithBatchingAsync_Csv_RestartsFromZero_WhenResumeDisabled_AndCheckpointHasProgress()
        {
                var filePath = Path.Combine(_tempDirectory, "restart.csv");
                File.WriteAllText(filePath,
                        "cpt_code,description,category,facility_name,facility_type,city,state,zip,insurer,negotiated_rate,rate_type,cash_price\n" +
                        "70551,Brain MRI,imaging,Test Hospital,hospital,Hoboken,NJ,07030,Plan A,1200,contracted,950\n" +
                        "70450,Head CT,imaging,Metro Clinic,clinic,Jersey City,NJ,07302,Plan B,900,contracted,700");

                using var dbContext = CreateDbContext();
                var persistence = new FakePricingPersistenceService();
                var checkpoints = new FakeCheckpointService(initialRowsProcessed: 9);
                var pipeline = new PricingIngestionPipeline(dbContext, persistence, checkpoints);

                var result = await pipeline.ImportFileWithBatchingAsync(filePath, 10, resumeFromCheckpoint: false, CancellationToken.None);

                Assert.True(result.Completed);
                Assert.Equal(0, result.RowsResumedFrom);
                Assert.Equal(2, result.RowsProcessed);
                Assert.Contains(0, checkpoints.SavedProgress);
                Assert.Contains(2, checkpoints.SavedProgress);
        }

        [Fact]
        public async Task ImportFileWithBatchingAsync_Json_FlushesInLoop_WhenBatchSizeReached()
        {
                var filePath = Path.Combine(_tempDirectory, "json-batch-flush.json");
                await File.WriteAllTextAsync(filePath, """
                [
                    {
                        "cpt_code": "70551",
                        "description": "Brain MRI",
                        "category": "imaging",
                        "facility_name": "Hospital A",
                        "facility_type": "hospital",
                        "city": "Hoboken",
                        "state": "NJ",
                        "zip": "07030",
                        "insurer": "Plan A",
                        "negotiated_rate": 1200,
                        "rate_type": "contracted",
                        "cash_price": 950
                    },
                    {
                        "cpt_code": "70450",
                        "description": "Head CT",
                        "category": "imaging",
                        "facility_name": "Hospital B",
                        "facility_type": "hospital",
                        "city": "Jersey City",
                        "state": "NJ",
                        "zip": "07302",
                        "insurer": "Plan B",
                        "negotiated_rate": 900,
                        "rate_type": "contracted",
                        "cash_price": 700
                    }
                ]
                """);

                using var dbContext = CreateDbContext();
                var persistence = new FakePricingPersistenceService();
                var checkpoints = new FakeCheckpointService();
                var pipeline = new PricingIngestionPipeline(dbContext, persistence, checkpoints);

                var result = await pipeline.ImportFileWithBatchingAsync(filePath, 1, resumeFromCheckpoint: true, CancellationToken.None);

                Assert.True(result.Completed);
                Assert.Equal(2, result.RowsProcessed);
                Assert.Equal(2, persistence.Calls.Count);
                Assert.Equal(new[] { 1, 2 }, checkpoints.SavedProgress.Where(x => x > 0).ToArray());
        }

            [Fact]
            public async Task ImportFileWithBatchingAsync_Csv_UsesFallbacksAndParsesInvalidDecimalsAsNull()
            {
                var filePath = Path.Combine(_tempDirectory, "fallbacks.csv");
                File.WriteAllText(filePath,
                    "cpt_code,negotiated_rate,cash_price\n" +
                    "70551,not-a-number,also-bad");

                using var dbContext = CreateDbContext();
                var persistence = new FakePricingPersistenceService();
                var checkpoints = new FakeCheckpointService();
                var pipeline = new PricingIngestionPipeline(dbContext, persistence, checkpoints);

                var result = await pipeline.ImportFileWithBatchingAsync(filePath, 10, resumeFromCheckpoint: true, CancellationToken.None);

                Assert.True(result.Completed);
                Assert.Single(persistence.Calls);
                var row = Assert.Single(persistence.Calls[0]);
                Assert.Equal("Unknown Procedure", row.ProcedureDescription);
                Assert.Equal("Unknown Facility", row.FacilityName);
                Assert.Equal("hospital", row.FacilityType);
                Assert.Null(row.NegotiatedRate);
                Assert.Null(row.CashPrice);
            }

        [Fact]
        public async Task ImportFileWithBatchingAsync_Json_RestartsFromZero_WhenResumeDisabled_AndCheckpointHasProgress()
        {
                var filePath = Path.Combine(_tempDirectory, "restart.json");
                await File.WriteAllTextAsync(filePath, """
                [
                    {
                        "cpt_code": "",
                        "description": "Invalid",
                        "category": "imaging",
                        "facility_name": "Hospital A",
                        "facility_type": "hospital",
                        "city": "Hoboken",
                        "state": "NJ",
                        "zip": "07030",
                        "insurer": "Plan A",
                        "negotiated_rate": 1200,
                        "rate_type": "contracted",
                        "cash_price": 950
                    },
                    {
                        "cpt_code": "70450",
                        "description": "Head CT",
                        "category": "imaging",
                        "facility_name": "Hospital B",
                        "facility_type": "hospital",
                        "city": "Jersey City",
                        "state": "NJ",
                        "zip": "07302",
                        "insurer": "Plan B",
                        "negotiated_rate": 900,
                        "rate_type": "contracted",
                        "cash_price": 700
                    }
                ]
                """);

                using var dbContext = CreateDbContext();
                var persistence = new FakePricingPersistenceService();
                var checkpoints = new FakeCheckpointService(initialRowsProcessed: 5);
                var pipeline = new PricingIngestionPipeline(dbContext, persistence, checkpoints);

                var result = await pipeline.ImportFileWithBatchingAsync(filePath, 10, resumeFromCheckpoint: false, CancellationToken.None);

                Assert.True(result.Completed);
                Assert.Equal(0, result.RowsResumedFrom);
                Assert.Equal(2, result.RowsProcessed);
                Assert.Contains(0, checkpoints.SavedProgress);
                Assert.Contains(2, checkpoints.SavedProgress);
                Assert.Single(persistence.Calls);
                Assert.Single(persistence.Calls[0]);
                Assert.Equal("70450", persistence.Calls[0][0].CptCode);
        }

    [Fact]
    public async Task StoreStructuredPricingDataAsync_DelegatesToPersistenceService()
    {
        using var dbContext = CreateDbContext();
        var persistence = new FakePricingPersistenceService();
        var checkpoints = new FakeCheckpointService();
        var pipeline = new PricingIngestionPipeline(dbContext, persistence, checkpoints);

        var input = new List<StructuredPricingRecord>
        {
            new()
            {
                CptCode = "70551",
                ProcedureDescription = "Brain MRI",
                ProcedureCategory = "imaging",
                FacilityName = "Test",
                FacilityType = "hospital",
                City = "Hoboken",
                State = "NJ",
                ZipCode = "07030",
                InsurerName = "PLAN A",
                NegotiatedRate = 1200,
                NegotiatedRateType = "contracted",
                CashPrice = 950,
                LastUpdated = DateTimeOffset.UtcNow
            }
        };

        var result = await pipeline.StoreStructuredPricingDataAsync(input, CancellationToken.None);

        Assert.Single(persistence.Calls);
        Assert.Equal(1, result.RecordsReceived);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed class FakePricingPersistenceService : IPricingPersistenceService
    {
        public List<IReadOnlyList<StructuredPricingRecord>> Calls { get; } = new();

        public Task<PricingPersistenceResult> UpsertPricingDataAsync(
            IReadOnlyList<StructuredPricingRecord> records,
            CancellationToken cancellationToken)
        {
            Calls.Add(records.ToList());
            return Task.FromResult(new PricingPersistenceResult
            {
                RecordsReceived = records.Count,
                NegotiatedRatesUpserted = records.Count,
                CashPricesUpserted = records.Count
            });
        }
    }

    private sealed class FakeCheckpointService(int initialRowsProcessed = 0) : IIngestionCheckpointService
    {
        private readonly IngestionCheckpointRecord _record = new()
        {
            CheckpointKey = Guid.NewGuid().ToString("N"),
            FilePath = string.Empty,
            BatchSize = 0,
            RowsProcessed = initialRowsProcessed,
            Status = "in_progress",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        public List<int> SavedProgress { get; } = new();
        public IngestionCheckpointRecord? Completed { get; private set; }

        public Task<IngestionCheckpointRecord> GetOrCreateAsync(string filePath, int batchSize, CancellationToken cancellationToken, string? fileHashSha256 = null)
        {
            _record.FilePath = Path.GetFullPath(filePath);
            _record.BatchSize = batchSize;
            return Task.FromResult(_record);
        }

        public Task<IngestionCheckpointRecord?> GetByKeyAsync(string checkpointKey, CancellationToken cancellationToken)
        {
            return Task.FromResult<IngestionCheckpointRecord?>(_record.CheckpointKey == checkpointKey ? _record : null);
        }

        public Task<IReadOnlyList<IngestionCheckpointRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken)
        {
            IReadOnlyList<IngestionCheckpointRecord> results = new[] { _record };
            return Task.FromResult(results);
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
            _record.RowsProcessed = rowsProcessed;
            _record.UpdatedAtUtc = DateTimeOffset.UtcNow;
            SavedProgress.Add(rowsProcessed);
            return Task.CompletedTask;
        }

        public Task MarkCompletedAsync(string checkpointKey, int rowsProcessed, CancellationToken cancellationToken)
        {
            _record.RowsProcessed = rowsProcessed;
            _record.Status = "completed";
            _record.UpdatedAtUtc = DateTimeOffset.UtcNow;
            Completed = new IngestionCheckpointRecord
            {
                CheckpointKey = _record.CheckpointKey,
                FilePath = _record.FilePath,
                BatchSize = _record.BatchSize,
                RowsProcessed = _record.RowsProcessed,
                Status = _record.Status,
                UpdatedAtUtc = _record.UpdatedAtUtc
            };
            return Task.CompletedTask;
        }
    }
}
