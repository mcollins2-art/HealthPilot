using HealthPilot.Api.Services;

namespace HealthPilot.Api.Ingestion;

/// <summary>
/// Projection of an <see cref="Models.IngestionCheckpoint"/> entity used in API responses and
/// checkpoint service return values. Decouples API contracts from the EF Core entity.
/// </summary>
public class IngestionCheckpointRecord
{
    /// <summary>SHA-256 hex hash of the normalized file path and batch size; serves as the unique key.</summary>
    public string CheckpointKey { get; set; } = string.Empty;

    /// <summary>Absolute, normalized path to the file being imported.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Batch size used for this import run.</summary>
    public int BatchSize { get; set; }

    /// <summary>Number of source rows processed so far; used as the resume offset on restart.</summary>
    public int RowsProcessed { get; set; }

    /// <summary>Lifecycle status of the checkpoint: <c>"in_progress"</c> or <c>"completed"</c>.</summary>
    public string Status { get; set; } = "in_progress";

    /// <summary>UTC timestamp of the most recent checkpoint update.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>
/// Result returned by <see cref="PricingIngestionPipeline.ImportFileWithBatchingAsync"/>
/// after a batched import completes or is checkpointed.
/// </summary>
public class IngestionBatchImportResult
{
    /// <summary>Aggregated persistence counts across all batches processed in this run.</summary>
    public PricingPersistenceResult Persistence { get; set; } = new();

    /// <summary>Checkpoint key associated with this import run.</summary>
    public string CheckpointKey { get; set; } = string.Empty;

    /// <summary>Row offset from which processing resumed (0 if starting fresh).</summary>
    public int RowsResumedFrom { get; set; }

    /// <summary>Total source rows seen during this run (including rows skipped due to the resume offset).</summary>
    public int RowsProcessed { get; set; }

    /// <summary><c>true</c> if the file was fully processed; <c>false</c> if processing was interrupted.</summary>
    public bool Completed { get; set; }
}