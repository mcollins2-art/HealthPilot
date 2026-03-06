namespace HealthPilot.Api.Dtos;

/// <summary>
/// Normalized, parser-agnostic representation of a single pricing record flowing from the
/// ingestion parsers to the persistence layer. Keeps ingestion logic decoupled from EF Core entities.
/// </summary>
public class StructuredPricingRecord
{
    /// <summary>Normalized CPT procedure code (uppercase, alphanumeric only).</summary>
    public required string CptCode { get; set; }

    /// <summary>Human-readable description of the procedure (e.g. "Brain MRI without contrast").</summary>
    public required string ProcedureDescription { get; set; }

    /// <summary>High-level category for the procedure (e.g. "imaging").</summary>
    public required string ProcedureCategory { get; set; }

    /// <summary>Name of the facility that published this pricing data.</summary>
    public required string FacilityName { get; set; }

    /// <summary>Type classification of the facility (e.g. "hospital", "outpatient").</summary>
    public required string FacilityType { get; set; }

    /// <summary>City where the facility is located.</summary>
    public required string City { get; set; }

    /// <summary>Two-letter state abbreviation (uppercase) where the facility is located.</summary>
    public required string State { get; set; }

    /// <summary>Five- or nine-digit ZIP code of the facility.</summary>
    public required string ZipCode { get; set; }

    /// <summary>Insurer name associated with the negotiated rate, or <c>null</c> for cash-price-only records.</summary>
    public string? InsurerName { get; set; }

    /// <summary>Negotiated rate between the insurer and the facility, or <c>null</c> if not applicable.</summary>
    public decimal? NegotiatedRate { get; set; }

    /// <summary>Rate type descriptor (e.g. "contracted", "fee_schedule"), or <c>null</c> if not provided.</summary>
    public string? NegotiatedRateType { get; set; }

    /// <summary>Self-pay/cash price at this facility, or <c>null</c> if not published.</summary>
    public decimal? CashPrice { get; set; }

    /// <summary>UTC timestamp of the most recent update to this pricing record.</summary>
    public DateTimeOffset LastUpdated { get; set; }
}
