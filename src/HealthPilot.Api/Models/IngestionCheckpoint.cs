namespace HealthPilot.Api.Models;

/// <summary>
/// Tracks the progress of a batched pricing file import, enabling checkpoint/resume behavior.
/// The natural key is a SHA-256 hash of the normalized file path and batch size.
/// </summary>
public class IngestionCheckpoint
{
    /// <summary>SHA-256 hex hash of "<c>{normalizedFilePath}|{batchSize}</c>"; serves as the primary key.</summary>
    public string CheckpointKey { get; set; } = string.Empty;

    /// <summary>Absolute, normalized path to the file being imported.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Batch size used for this import run.</summary>
    public int BatchSize { get; set; }

    /// <summary>Number of source rows processed so far. Used as the resume offset on restart.</summary>
    public int RowsProcessed { get; set; }

    /// <summary>Lifecycle status of the checkpoint: <c>"in_progress"</c> or <c>"completed"</c>.</summary>
    public string Status { get; set; } = "in_progress";

    /// <summary>UTC timestamp of the most recent checkpoint update.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
