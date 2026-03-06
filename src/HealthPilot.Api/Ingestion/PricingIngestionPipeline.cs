using HealthPilot.Api.Dtos;
using HealthPilot.Api.Ingestion.Parsers;
using HealthPilot.Api.Services;

namespace HealthPilot.Api.Ingestion;

/// <summary>
/// Orchestrates the end-to-end pricing import workflow: file parsing, batched persistence,
/// and checkpoint management. Supports both in-memory (full-file) and streaming (batched)
/// import modes for CSV and JSON CMS machine-readable pricing files.
/// </summary>
public class PricingIngestionPipeline(
    IPricingPersistenceService pricingPersistenceService,
    IIngestionCheckpointService checkpointService)
{
    /// <summary>
    /// Selects and invokes the appropriate parser based on the file extension.
    /// </summary>
    /// <param name="filePath">Absolute path to the pricing file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of normalized pricing records.</returns>
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

    /// <summary>
    /// Persists a pre-parsed list of structured pricing records through the persistence service.
    /// </summary>
    /// <param name="records">Normalized records from a prior <see cref="ParseAsync"/> call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Counts of entities created and rows upserted.</returns>
    public async Task<PricingPersistenceResult> StoreStructuredPricingDataAsync(
        IReadOnlyList<StructuredPricingRecord> records,
        CancellationToken cancellationToken)
    {
        return await pricingPersistenceService.UpsertPricingDataAsync(records, cancellationToken);
    }

    /// <summary>
    /// Imports a pricing file using streaming batch processing with checkpoint/resume support.
    /// Dispatches to CSV or JSON batched loader based on file extension.
    /// </summary>
    /// <param name="filePath">Absolute path to the .csv or .json pricing file.</param>
    /// <param name="batchSize">Number of records per persistence batch (must be ≥ 1).</param>
    /// <param name="resumeFromCheckpoint">
    /// When <c>true</c>, resumes from the last saved checkpoint offset;
    /// when <c>false</c>, resets the checkpoint and processes from the beginning.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="IngestionBatchImportResult"/> with aggregate counts and checkpoint key.</returns>
    public async Task<IngestionBatchImportResult> ImportFileWithBatchingAsync(
        string filePath,
        int batchSize,
        bool resumeFromCheckpoint,
        CancellationToken cancellationToken)
    {
        if (batchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be >= 1.");
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".csv" => await ImportCsvWithBatchingAsync(filePath, batchSize, resumeFromCheckpoint, cancellationToken),
            ".json" => await ImportJsonWithBatchingAsync(filePath, batchSize, resumeFromCheckpoint, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported file extension: {extension}")
        };
    }

    private async Task<IngestionBatchImportResult> ImportCsvWithBatchingAsync(
        string filePath,
        int batchSize,
        bool resumeFromCheckpoint,
        CancellationToken cancellationToken)
    {
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

            var mapped = MapRow(row);
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
        CancellationToken cancellationToken)
    {
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

            var mapped = MapRow(row);
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

    private static StructuredPricingRecord? MapRow(Dictionary<string, string> row)
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
