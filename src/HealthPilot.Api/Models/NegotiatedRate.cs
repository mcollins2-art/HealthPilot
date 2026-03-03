namespace HealthPilot.Api.Models;

public class NegotiatedRate
{
    public int ProcedureId { get; set; }
    public int FacilityId { get; set; }
    public int InsurerId { get; set; }

    public decimal Rate { get; set; }
    public required string RateType { get; set; }
    public DateTimeOffset LastUpdated { get; set; }

    public Procedure Procedure { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Insurer Insurer { get; set; } = null!;
}
