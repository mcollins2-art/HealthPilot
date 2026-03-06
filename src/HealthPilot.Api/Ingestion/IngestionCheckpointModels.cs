using HealthPilot.Api.Services;

namespace HealthPilot.Api.Ingestion;

public class IngestionCheckpointRecord
{
    public string CheckpointKey { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int BatchSize { get; set; }
    public int RowsProcessed { get; set; }
    public string Status { get; set; } = "in_progress";
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public class IngestionBatchImportResult
{
    public PricingPersistenceResult Persistence { get; set; } = new();
    public string CheckpointKey { get; set; } = string.Empty;
    public int RowsResumedFrom { get; set; }
    public int RowsProcessed { get; set; }
    public List<IngestionRowParseError> ParseErrors { get; set; } = [];
    public bool Completed { get; set; }
}

public class IngestionRowParseError
{
    public int? CsvRowNumber { get; set; }
    public string? JsonPath { get; set; }
    public string Message { get; set; } = string.Empty;
}
