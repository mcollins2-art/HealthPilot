namespace HealthPilot.Api.Models;

public class CashPrice
{
    public int ProcedureId { get; set; }
    public int FacilityId { get; set; }

    public decimal CashPriceAmount { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
    public long? IngestionJobId { get; set; }

    public Procedure Procedure { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public IngestionJob? IngestionJob { get; set; }
}
