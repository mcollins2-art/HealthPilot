namespace HealthPilot.Api.Models;

/// <summary>
/// Represents a durable record of a pricing file import operation, supporting async processing,
/// retry with dead-letter handling, deterministic replay, and provenance tracking.
/// </summary>
public class IngestionJob
{
    /// <summary>Auto-generated surrogate key.</summary>
    public long Id { get; set; }

    /// <summary>UTC timestamp when this job was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>UTC timestamp of the most recent status update.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>
    /// Current lifecycle status of the job.
    /// Valid values: <c>"queued"</c>, <c>"in_progress"</c>, <c>"completed"</c>, <c>"dead_lettered"</c>.
    /// </summary>
    public string Status { get; set; } = "queued";

    /// <summary>Absolute path to the file being imported.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Batch size used for this import run.</summary>
    public int BatchSize { get; set; }

    /// <summary>Whether the worker should resume from the last saved checkpoint instead of reprocessing from row 0.</summary>
    public bool ResumeFromCheckpoint { get; set; }

    /// <summary>Checkpoint key associated with this job, populated after a successful or partial import.</summary>
    public string? CheckpointKey { get; set; }

    /// <summary>Number of source rows seen (including skipped rows).</summary>
    public int RowsProcessed { get; set; }

    /// <summary>Number of records that passed structural validation and were submitted to the persistence layer.</summary>
    public int RecordsReceived { get; set; }

    /// <summary>Number of records that were dropped due to invalid or missing required fields.</summary>
    public int RecordsSkipped { get; set; }

    /// <summary>Number of processing attempts made so far.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Maximum allowed attempts before the job is moved to <c>"dead_lettered"</c> status.</summary>
    public int MaxAttempts { get; set; } = 2;

    /// <summary>Most recent error message, if any. Populated when the job fails or is dead-lettered.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>If this is a replay job, the <see cref="Id"/> of the original job being replayed.</summary>
    public long? ReplayOfJobId { get; set; }

    /// <summary>Optional free-text identifier for the upstream system that submitted this file.</summary>
    public string? SourceSystem { get; set; }

    /// <summary>Lowercase hex SHA-256 hash of the file content at import time, used for deduplication and auditing.</summary>
    public string? FileHashSha256 { get; set; }

    /// <summary>Parser version identifier used to process this file (e.g. <c>"cms_csv_v1"</c>).</summary>
    public string ParserVersion { get; set; } = "cms_v1";

    /// <summary>UTC timestamp from which the pricing data in this file is considered effective.</summary>
    public DateTimeOffset? EffectiveStartUtc { get; set; }

    /// <summary>UTC timestamp after which the pricing data in this file is no longer effective.</summary>
    public DateTimeOffset? EffectiveEndUtc { get; set; }

    /// <summary>Tenant identifier associated with this job, if the import was submitted by a tenant-scoped API key.</summary>
    public string? TenantId { get; set; }
}
