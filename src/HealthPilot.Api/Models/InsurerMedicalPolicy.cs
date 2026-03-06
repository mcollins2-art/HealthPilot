namespace HealthPilot.Api.Models;

public class InsurerMedicalPolicy
{
    public int Id { get; set; }
    public int InsurerId { get; set; }
    public required string PolicyName { get; set; }
    public required string SourceUrl { get; set; }

    public Insurer? Insurer { get; set; }
    public ICollection<PolicyVersion> Versions { get; set; } = new List<PolicyVersion>();
}
