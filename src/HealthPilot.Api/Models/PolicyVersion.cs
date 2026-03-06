namespace HealthPilot.Api.Models;

public class PolicyVersion
{
    public int Id { get; set; }
    public int PolicyId { get; set; }
    public DateOnly VersionDate { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public required string RawDocumentPath { get; set; }

    public InsurerMedicalPolicy? Policy { get; set; }
    public ICollection<PolicyRule> Rules { get; set; } = new List<PolicyRule>();
    public ICollection<PolicyProcedureMapping> ProcedureMappings { get; set; } = new List<PolicyProcedureMapping>();
}
