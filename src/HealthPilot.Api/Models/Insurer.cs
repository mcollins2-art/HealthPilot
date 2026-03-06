namespace HealthPilot.Api.Models;

public class Insurer
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public ICollection<NegotiatedRate> NegotiatedRates { get; set; } = new List<NegotiatedRate>();
    public ICollection<InsurerMedicalPolicy> MedicalPolicies { get; set; } = new List<InsurerMedicalPolicy>();
}
