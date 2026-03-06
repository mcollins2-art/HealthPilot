using System.ComponentModel.DataAnnotations;

namespace HealthPilot.Api.Dtos;

public class AuthorizationEstimateRequest
{
    [Required]
    [MinLength(2)]
    public required string Insurer { get; set; }

    [Required]
    [MinLength(4)]
    [MaxLength(10)]
    public required string ProcedureCpt { get; set; }

    [Required]
    [MinLength(3)]
    [MaxLength(12)]
    public required string DiagnosisIcd10 { get; set; }

    [Range(0, 120)]
    public int Age { get; set; }
}
