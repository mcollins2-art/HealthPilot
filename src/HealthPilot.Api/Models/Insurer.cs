namespace HealthPilot.Api.Models;

/// <summary>
/// Represents a health insurance company or payer that has negotiated rates with facilities.
/// </summary>
public class Insurer
{
    /// <summary>Auto-generated surrogate key.</summary>
    public int Id { get; set; }

    /// <summary>Insurer name as it appears in the machine-readable pricing file (normalized to uppercase).</summary>
    public required string Name { get; set; }

    /// <summary>Negotiated rates this insurer has established with facilities.</summary>
    public ICollection<NegotiatedRate> NegotiatedRates { get; set; } = new List<NegotiatedRate>();
}
