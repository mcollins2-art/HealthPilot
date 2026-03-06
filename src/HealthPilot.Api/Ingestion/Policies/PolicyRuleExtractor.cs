namespace HealthPilot.Api.Ingestion.Policies;

public class PolicyRuleExtractor : IPolicyRuleExtractor
{
    public IReadOnlyList<ExtractedPolicyRule> Extract(string normalizedPolicyText, IReadOnlyList<string> procedureCptCodes)
    {
        var rules = new List<ExtractedPolicyRule>();
        var normalizedText = normalizedPolicyText.Replace(Environment.NewLine, " ");
        var lowerText = normalizedText.ToLowerInvariant();

        if (lowerText.Contains("medically necessary") || lowerText.Contains("prior authorization"))
        {
            foreach (var cptCode in procedureCptCodes.DefaultIfEmpty(string.Empty))
            {
                if (lowerText.Contains("conservative therapy") && lowerText.Contains("six weeks"))
                {
                    rules.Add(new ExtractedPolicyRule(
                        "authorization_required",
                        "Conservative therapy is required before authorization.",
                        true,
                        100,
                        string.IsNullOrWhiteSpace(cptCode) ? null : cptCode,
                        "6 weeks conservative therapy",
                        "No documented conservative treatment"));
                }

                if (lowerText.Contains("neurological deficit"))
                {
                    rules.Add(new ExtractedPolicyRule(
                        "clinical_criteria",
                        "Neurological deficit documentation is required.",
                        true,
                        110,
                        string.IsNullOrWhiteSpace(cptCode) ? null : cptCode,
                        "neurological deficit documentation",
                        "No neurological deficit documentation"));
                }
            }
        }

        return rules;
    }
}
