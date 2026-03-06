namespace HealthPilot.Api.Models;

public class IngestionJob
{
    public long Id { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string Status { get; set; } = "queued";
    public string FilePath { get; set; } = string.Empty;
    public int BatchSize { get; set; }
    public bool ResumeFromCheckpoint { get; set; }
    public string? CheckpointKey { get; set; }
    public int RowsProcessed { get; set; }
    public int RecordsReceived { get; set; }
    public int RecordsSkipped { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 2;
    public string? ErrorMessage { get; set; }
    public long? ReplayOfJobId { get; set; }
    public string? SourceSystem { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? FileHashSha256 { get; set; }
    public string ParserVersion { get; set; } = "cms_v1";
    public DateTimeOffset? EffectiveStartUtc { get; set; }
    public DateTimeOffset? EffectiveEndUtc { get; set; }
    public string? TenantId { get; set; }
}
