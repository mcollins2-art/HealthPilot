namespace HealthPilot.Api.Dtos;

public class AuthorizationEstimateResponse
{
    public bool AuthorizationRequired { get; set; }
    public decimal ApprovalProbability { get; set; }
    public List<string> RequiredConditions { get; set; } = [];
    public string? CommonDenialReason { get; set; }
}
