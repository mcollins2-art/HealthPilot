using System.ComponentModel.DataAnnotations;

namespace HealthPilot.Api.Dtos;

public class IngestionImportRequest
{
    [Required]
    [MinLength(3)]
    public required string FilePath { get; set; }

    [Range(1, 50000)]
    public int? BatchSize { get; set; }

    public bool ResumeFromCheckpoint { get; set; } = true;
    public bool Async { get; set; }
    [MaxLength(128)]
    public string? IdempotencyKey { get; set; }
    public string? SourceSystem { get; set; }
    public DateTimeOffset? EffectiveStartUtc { get; set; }
    public DateTimeOffset? EffectiveEndUtc { get; set; }
}
