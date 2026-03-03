namespace HealthPilot.Api.Models;

public class IngestionCheckpoint
{
    public string CheckpointKey { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int BatchSize { get; set; }
    public int RowsProcessed { get; set; }
    public string Status { get; set; } = "in_progress";
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
