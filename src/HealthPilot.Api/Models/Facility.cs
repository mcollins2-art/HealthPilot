namespace HealthPilot.Api.Models;

/// <summary>
/// Represents a healthcare facility that publishes pricing data.
/// The natural unique key is <c>(Name, City, State, Zip)</c>.
/// </summary>
public class Facility
{
    /// <summary>Auto-generated surrogate key.</summary>
    public int Id { get; set; }

    /// <summary>Facility name as it appears in the machine-readable pricing file.</summary>
    public required string Name { get; set; }

    /// <summary>Facility type classification (e.g. "hospital", "outpatient", "clinic").</summary>
    public required string Type { get; set; }

    /// <summary>City where the facility is located.</summary>
    public required string City { get; set; }

    /// <summary>Two-letter state abbreviation (uppercase).</summary>
    public required string State { get; set; }

    /// <summary>Five- or nine-digit ZIP code of the facility.</summary>
    public required string Zip { get; set; }

    /// <summary>Negotiated rates published by this facility.</summary>
    public ICollection<NegotiatedRate> NegotiatedRates { get; set; } = new List<NegotiatedRate>();

    /// <summary>Self-pay cash prices published by this facility.</summary>
    public ICollection<CashPrice> CashPrices { get; set; } = new List<CashPrice>();
}
