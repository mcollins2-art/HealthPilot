namespace HealthPilot.Api.Ingestion.Policies;

public interface IPolicyRuleExtractor
{
    IReadOnlyList<ExtractedPolicyRule> Extract(string normalizedPolicyText, IReadOnlyList<string> procedureCptCodes);
}
