using System.Globalization;
using HealthPilot.Api.Data;
using HealthPilot.Api.Dtos;
using HealthPilot.Api.Ingestion.Parsers;
using HealthPilot.Api.Services;

namespace HealthPilot.Api.Ingestion;

public class PricingIngestionPipeline(
    AppDbContext dbContext,
    IPricingPersistenceService pricingPersistenceService,
    IIngestionCheckpointService checkpointService,
    IPricingParserRegistry parserRegistry)
{
    // Dispatches parser based on file type and returns normalized records.
    public async Task<IReadOnlyList<StructuredPricingRecord>> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        IPricingParser parser = parserRegistry.ResolveByExtension(extension);

        return await parser.ParseAsync(filePath, cancellationToken);
    }

    // Placeholder for scalable upsert orchestration into normalized pricing tables.
    public async Task<PricingPersistenceResult> StoreStructuredPricingDataAsync(
        IReadOnlyList<StructuredPricingRecord> records,
        CancellationToken cancellationToken)
    {
        _ = dbContext;
        return await pricingPersistenceService.UpsertPricingDataAsync(records, cancellationToken);
    }

    public async Task<IngestionBatchImportResult> ImportFileWithBatchingAsync(
        string filePath,
        int batchSize,
        bool resumeFromCheckpoint,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        if (batchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be >= 1.");
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".csv" => await ImportCsvWithBatchingAsync(filePath, batchSize, resumeFromCheckpoint, tenantId, cancellationToken),
            ".json" => await ImportJsonWithBatchingAsync(filePath, batchSize, resumeFromCheckpoint, tenantId, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported file extension: {extension}")
        };
    }

    private async Task<IngestionBatchImportResult> ImportCsvWithBatchingAsync(
        string filePath,
        int batchSize,
        bool resumeFromCheckpoint,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        var sourceLastUpdated = File.GetLastWriteTimeUtc(filePath);
        var checkpoint = await checkpointService.GetOrCreateAsync(filePath, batchSize, cancellationToken);
        var resumeOffset = resumeFromCheckpoint ? checkpoint.RowsProcessed : 0;

        if (!resumeFromCheckpoint && checkpoint.RowsProcessed != 0)
        {
            await checkpointService.SaveProgressAsync(checkpoint.CheckpointKey, 0, cancellationToken);
        }

        var aggregate = new PricingPersistenceResult();
        var batch = new List<StructuredPricingRecord>(batchSize);
        var rowsSeen = 0;

        foreach (var row in PricingLoader.StreamCsvRows(filePath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            rowsSeen++;
            if (rowsSeen <= resumeOffset)
            {
                continue;
            }

            var mapped = MapRow(row, sourceLastUpdated, tenantId);
            if (mapped is null)
            {
                continue;
            }

            batch.Add(mapped);

            if (batch.Count >= batchSize)
            {
                var batchResult = await pricingPersistenceService.UpsertPricingDataAsync(batch, cancellationToken);
                MergeResult(aggregate, batchResult);
                batch.Clear();
                await checkpointService.SaveProgressAsync(checkpoint.CheckpointKey, rowsSeen, cancellationToken);
            }
        }

        if (batch.Count > 0)
        {
            var batchResult = await pricingPersistenceService.UpsertPricingDataAsync(batch, cancellationToken);
            MergeResult(aggregate, batchResult);
            await checkpointService.SaveProgressAsync(checkpoint.CheckpointKey, rowsSeen, cancellationToken);
        }

        await checkpointService.MarkCompletedAsync(checkpoint.CheckpointKey, rowsSeen, cancellationToken);

        return new IngestionBatchImportResult
        {
            Persistence = aggregate,
            CheckpointKey = checkpoint.CheckpointKey,
            RowsResumedFrom = resumeOffset,
            RowsProcessed = rowsSeen,
            Completed = true
        };
    }

    private async Task<IngestionBatchImportResult> ImportJsonWithBatchingAsync(
        string filePath,
        int batchSize,
        bool resumeFromCheckpoint,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        var sourceLastUpdated = File.GetLastWriteTimeUtc(filePath);
        var checkpoint = await checkpointService.GetOrCreateAsync(filePath, batchSize, cancellationToken);
        var resumeOffset = resumeFromCheckpoint ? checkpoint.RowsProcessed : 0;

        if (!resumeFromCheckpoint && checkpoint.RowsProcessed != 0)
        {
            await checkpointService.SaveProgressAsync(checkpoint.CheckpointKey, 0, cancellationToken);
        }

        var aggregate = new PricingPersistenceResult();
        var batch = new List<StructuredPricingRecord>(batchSize);
        var rowsSeen = 0;

        await foreach (var row in PricingLoader.StreamJsonRowsAsync(filePath, cancellationToken))
        {
            rowsSeen++;
            if (rowsSeen <= resumeOffset)
            {
                continue;
            }

            var mapped = MapRow(row, sourceLastUpdated, tenantId);
            if (mapped is null)
            {
                continue;
            }

            batch.Add(mapped);

            if (batch.Count >= batchSize)
            {
                var batchResult = await pricingPersistenceService.UpsertPricingDataAsync(batch, cancellationToken);
                MergeResult(aggregate, batchResult);
                batch.Clear();
                await checkpointService.SaveProgressAsync(checkpoint.CheckpointKey, rowsSeen, cancellationToken);
            }
        }

        if (batch.Count > 0)
        {
            var batchResult = await pricingPersistenceService.UpsertPricingDataAsync(batch, cancellationToken);
            MergeResult(aggregate, batchResult);
            await checkpointService.SaveProgressAsync(checkpoint.CheckpointKey, rowsSeen, cancellationToken);
        }

        await checkpointService.MarkCompletedAsync(checkpoint.CheckpointKey, rowsSeen, cancellationToken);

        return new IngestionBatchImportResult
        {
            Persistence = aggregate,
            CheckpointKey = checkpoint.CheckpointKey,
            RowsResumedFrom = resumeOffset,
            RowsProcessed = rowsSeen,
            Completed = true
        };
    }

    private static StructuredPricingRecord? MapRow(Dictionary<string, string> row, DateTimeOffset sourceLastUpdated, string? tenantId)
    {
        string cpt = Normalizers.NormalizeCptCode(GetValue(row, "cpt_code"));
        if (string.IsNullOrWhiteSpace(cpt))
        {
            return null;
        }

        return new StructuredPricingRecord
        {
            CptCode = cpt,
            ProcedureDescription = GetValue(row, "description", "Unknown Procedure"),
            ProcedureCategory = GetValue(row, "category", "imaging"),
            FacilityName = GetValue(row, "facility_name", "Unknown Facility"),
            FacilityType = GetValue(row, "facility_type", "hospital"),
            City = GetValue(row, "city"),
            State = GetValue(row, "state"),
            ZipCode = GetValue(row, "zip"),
            InsurerName = Normalizers.NormalizeInsurerName(GetValue(row, "insurer")),
            NegotiatedRate = ParseDecimal(GetValue(row, "negotiated_rate")),
            NegotiatedRateType = GetValue(row, "rate_type"),
            CashPrice = ParseDecimal(GetValue(row, "cash_price")),
            LastUpdated = ParseDateTimeOffset(GetValue(row, "last_updated")) ?? sourceLastUpdated,
            TenantId = tenantId
        };
    }

    private static string GetValue(Dictionary<string, string> row, string key, string fallback = "")
    {
        return row.TryGetValue(key, out var value) ? value : fallback;
    }

    private static decimal? ParseDecimal(string value) => Normalizers.ParseDecimalInvariantOrNull(value);

    private static DateTimeOffset? ParseDateTimeOffset(string value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static void MergeResult(PricingPersistenceResult aggregate, PricingPersistenceResult batchResult)
    {
        aggregate.RecordsReceived += batchResult.RecordsReceived;
        aggregate.RecordsSkipped += batchResult.RecordsSkipped;
        aggregate.ProceduresCreated += batchResult.ProceduresCreated;
        aggregate.FacilitiesCreated += batchResult.FacilitiesCreated;
        aggregate.InsurersCreated += batchResult.InsurersCreated;
        aggregate.NegotiatedRatesUpserted += batchResult.NegotiatedRatesUpserted;
        aggregate.CashPricesUpserted += batchResult.CashPricesUpserted;
    }
}
