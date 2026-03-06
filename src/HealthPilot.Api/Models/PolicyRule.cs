namespace HealthPilot.Api.Models;

public class PolicyRule
{
    public int Id { get; set; }
    public int PolicyVersionId { get; set; }
    public required string RuleType { get; set; }
    public required string Description { get; set; }
    public bool Required { get; set; }
    public int Priority { get; set; }
    public string? ProcedureCptCode { get; set; }
    public string? ConditionExpression { get; set; }
    public string? DenialReason { get; set; }

    public PolicyVersion? PolicyVersion { get; set; }
}
