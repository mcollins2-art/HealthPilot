namespace HealthPilot.Api.Models;

public class OutpatientSurgeryProcedure
{
    public int Id { get; set; }
    public required string CptCode { get; set; }
    public required string Description { get; set; }
}
