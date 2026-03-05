namespace HealthPilot.Api.Models;

/// <summary>
/// Represents a medical procedure identified by its CPT code.
/// </summary>
public class Procedure
{
    /// <summary>Auto-generated surrogate key.</summary>
    public int Id { get; set; }

    /// <summary>CPT procedure code (normalized to uppercase, max 10 characters).</summary>
    public required string CptCode { get; set; }

    /// <summary>Human-readable description of the procedure (e.g. "Brain MRI without contrast").</summary>
    public required string Description { get; set; }

    /// <summary>High-level category grouping (e.g. "imaging").</summary>
    public required string Category { get; set; }

    /// <summary>Negotiated rates associated with this procedure across all facilities and insurers.</summary>
    public ICollection<NegotiatedRate> NegotiatedRates { get; set; } = new List<NegotiatedRate>();

    /// <summary>Cash prices associated with this procedure across all facilities.</summary>
    public ICollection<CashPrice> CashPrices { get; set; } = new List<CashPrice>();
}
