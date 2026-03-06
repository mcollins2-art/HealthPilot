namespace HealthPilot.Api.Models;

public class DiagnosisProcedureMapping
{
    public int Id { get; set; }
    public required string Icd10Code { get; set; }
    public required string CptCode { get; set; }
    public decimal RelevanceScore { get; set; }
}
