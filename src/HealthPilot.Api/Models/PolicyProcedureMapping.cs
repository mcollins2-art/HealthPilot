namespace HealthPilot.Api.Models;

public class PolicyProcedureMapping
{
    public int Id { get; set; }
    public int PolicyVersionId { get; set; }
    public int ProcedureId { get; set; }

    public PolicyVersion? PolicyVersion { get; set; }
    public Procedure? Procedure { get; set; }
}
