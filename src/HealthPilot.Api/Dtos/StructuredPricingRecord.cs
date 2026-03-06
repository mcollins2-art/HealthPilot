namespace HealthPilot.Api.Dtos;

// Typed record that flows from parsing to persistence, keeping ingestion logic
// independent from EF Core entity concerns.
public class StructuredPricingRecord
{
    public required string CptCode { get; set; }
    public required string ProcedureDescription { get; set; }
    public required string ProcedureCategory { get; set; }
    public required string FacilityName { get; set; }
    public required string FacilityType { get; set; }
    public required string City { get; set; }
    public required string State { get; set; }
    public required string ZipCode { get; set; }
    public string? InsurerName { get; set; }
    public decimal? NegotiatedRate { get; set; }
    public string? NegotiatedRateType { get; set; }
    public string? PolicyVersion { get; set; }
    public DateTimeOffset? EffectiveStartUtc { get; set; }
    public DateTimeOffset? EffectiveEndUtc { get; set; }
    public decimal? CashPrice { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
}
