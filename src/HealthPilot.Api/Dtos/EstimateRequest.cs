using System.ComponentModel.DataAnnotations;

namespace HealthPilot.Api.Dtos;

/// <summary>
/// Request body for the <c>POST /estimate</c> endpoint.
/// All benefit parameters must be non-negative; <see cref="CoinsurancePercent"/> must be in [0, 100].
/// </summary>
public class EstimateRequest
{
    /// <summary>Five- or nine-digit ZIP code of the service location (e.g. "90210" or "90210-1234").</summary>
    [Required]
    [MinLength(5)]
    [MaxLength(10)]
    public required string ZipCode { get; set; }

    /// <summary>Insurer name as it appears in the pricing data (case-insensitive match is applied).</summary>
    [Required]
    [MinLength(2)]
    [MaxLength(255)]
    public required string Insurer { get; set; }

    /// <summary>CPT procedure code (4–7 alphanumeric characters; normalized to uppercase).</summary>
    [Required]
    [MinLength(4)]
    [MaxLength(10)]
    public required string CptCode { get; set; }

    /// <summary>Member's remaining deductible balance. Defaults to <c>0</c> (fully met).</summary>
    [Range(0, double.MaxValue)]
    public decimal DeductibleRemaining { get; set; }

    /// <summary>Member's coinsurance percentage in the range [0, 100]. Defaults to <c>0</c>.</summary>
    [Range(0, 100)]
    public decimal CoinsurancePercent { get; set; }

    /// <summary>Fixed copay amount for this service type. Defaults to <c>0</c>.</summary>
    [Range(0, double.MaxValue)]
    public decimal Copay { get; set; }

    /// <summary>Member's remaining out-of-pocket maximum. Defaults to <c>0</c> (fully met).</summary>
    [Range(0, double.MaxValue)]
    public decimal OopMaxRemaining { get; set; }

    /// <summary>
    /// When <c>true</c> (default), copay is applied before the deductible—the most common plan design.
    /// When <c>false</c>, deductible is applied first and copay is added afterward.
    /// </summary>
    public bool CopayAppliesBeforeDeductible { get; set; } = true;
}
