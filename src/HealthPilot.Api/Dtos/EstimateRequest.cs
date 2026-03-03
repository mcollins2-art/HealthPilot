using System.ComponentModel.DataAnnotations;

namespace HealthPilot.Api.Dtos;

public class EstimateRequest
{
    [Required]
    [MinLength(5)]
    [MaxLength(10)]
    public required string ZipCode { get; set; }

    [Required]
    [MinLength(2)]
    public required string Insurer { get; set; }

    [Required]
    [MinLength(4)]
    [MaxLength(10)]
    public required string CptCode { get; set; }

    [Range(0, double.MaxValue)]
    public decimal DeductibleRemaining { get; set; }

    [Range(0, 100)]
    public decimal CoinsurancePercent { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Copay { get; set; }

    [Range(0, double.MaxValue)]
    public decimal OopMaxRemaining { get; set; }

    // Defaults to true for the most common adjudication pattern used by plans.
    public bool CopayAppliesBeforeDeductible { get; set; } = true;
}
