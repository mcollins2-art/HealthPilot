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
}
