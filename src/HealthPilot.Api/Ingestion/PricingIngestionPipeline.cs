using HealthPilot.Api.Data;
using HealthPilot.Api.Dtos;
using HealthPilot.Api.Ingestion.Parsers;
using HealthPilot.Api.Services;

namespace HealthPilot.Api.Ingestion;

public class PricingIngestionPipeline(
    AppDbContext dbContext,
    IPricingPersistenceService pricingPersistenceService,
    IIngestionCheckpointService checkpointService,
    ILogger<PricingIngestionPipeline>? logger = null)
{
    private readonly ILogger<PricingIngestionPipeline> _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PricingIngestionPipeline>.Instance;
    // Dispatches parser based on file type and returns normalized records.
    public async Task<IReadOnlyList<StructuredPricingRecord>> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        IPricingParser parser = Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".json" => new CmsJsonPricingParser(),
            ".csv" => new CmsCsvPricingParser(),
            _ => throw new NotSupportedException($"Unsupported file extension: {Path.GetExtension(filePath)}")
        };

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
        CancellationToken cancellationToken,
        string? fileHashSha256 = null)
    {
        if (batchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be >= 1.");
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".csv" => await ImportCsvWithBatchingAsync(filePath, batchSize, resumeFromCheckpoint, cancellationToken, fileHashSha256),
            ".json" => await ImportJsonWithBatchingAsync(filePath, batchSize, resumeFromCheckpoint, cancellationToken, fileHashSha256),
            _ => throw new NotSupportedException($"Unsupported file extension: {extension}")
        };
    }

    private async Task<IngestionBatchImportResult> ImportCsvWithBatchingAsync(
        string filePath,
        int batchSize,
        bool resumeFromCheckpoint,
        CancellationToken cancellationToken,
        string? fileHashSha256)
    {
        var checkpoint = await checkpointService.GetOrCreateAsync(filePath, batchSize, cancellationToken, fileHashSha256);
        var resumeOffset = resumeFromCheckpoint ? checkpoint.RowsProcessed : 0;

        if (!resumeFromCheckpoint && checkpoint.RowsProcessed != 0)
        {
            await checkpointService.SaveProgressAsync(checkpoint.CheckpointKey, 0, cancellationToken);
        }

        var aggregate = new PricingPersistenceResult();
        var batch = new List<StructuredPricingRecord>(batchSize);
        var parseErrors = new List<IngestionRowParseError>();
        var rowsSeen = 0;

        foreach (var row in PricingLoader.StreamCsvRows(filePath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            rowsSeen++;
            if (rowsSeen <= resumeOffset)
            {
                continue;
            }

            var mapped = MapRow(row, csvRowNumber: rowsSeen + 1, jsonPath: null, parseErrors);
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
            ParseErrors = parseErrors,
            Completed = true
        };
    }

    private async Task<IngestionBatchImportResult> ImportJsonWithBatchingAsync(
        string filePath,
        int batchSize,
        bool resumeFromCheckpoint,
        CancellationToken cancellationToken,
        string? fileHashSha256)
    {
        var checkpoint = await checkpointService.GetOrCreateAsync(filePath, batchSize, cancellationToken, fileHashSha256);
        var resumeOffset = resumeFromCheckpoint ? checkpoint.RowsProcessed : 0;

        if (!resumeFromCheckpoint && checkpoint.RowsProcessed != 0)
        {
            await checkpointService.SaveProgressAsync(checkpoint.CheckpointKey, 0, cancellationToken);
        }

        var aggregate = new PricingPersistenceResult();
        var batch = new List<StructuredPricingRecord>(batchSize);
        var parseErrors = new List<IngestionRowParseError>();
        var rowsSeen = 0;

        await foreach (var row in PricingLoader.StreamJsonRowsAsync(filePath, cancellationToken))
        {
            rowsSeen++;
            if (rowsSeen <= resumeOffset)
            {
                continue;
            }

            var mapped = MapRow(row, csvRowNumber: null, jsonPath: $"$[{rowsSeen - 1}]", parseErrors);
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
            ParseErrors = parseErrors,
            Completed = true
        };
    }

    private StructuredPricingRecord? MapRow(
        Dictionary<string, string> row,
        int? csvRowNumber,
        string? jsonPath,
        List<IngestionRowParseError> parseErrors)
    {
        string cpt = Normalizers.NormalizeCptCode(GetValue(row, "cpt_code"));
        if (!Normalizers.IsValidCptOrHcpcs(cpt))
        {
            var parseError = new IngestionRowParseError
            {
                CsvRowNumber = csvRowNumber,
                JsonPath = jsonPath,
                Message = "Invalid CPT/HCPCS format."
            };
            parseErrors.Add(parseError);
            _logger.LogWarning(
                "Skipping ingestion row with invalid CPT/HCPCS. CsvRow={CsvRowNumber}, JsonPath={JsonPath}, CptCode={CptCode}",
                csvRowNumber,
                jsonPath,
                cpt);
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
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    private static string GetValue(Dictionary<string, string> row, string key, string fallback = "")
    {
        return row.TryGetValue(key, out var value) ? value : fallback;
    }

    private static decimal? ParseDecimal(string value)
    {
        return decimal.TryParse(value, out var parsed) ? parsed : null;
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
