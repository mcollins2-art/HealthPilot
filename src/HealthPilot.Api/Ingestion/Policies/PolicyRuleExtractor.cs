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
            var cptCodes = procedureCptCodes.Count > 0 ? procedureCptCodes : [string.Empty];
            foreach (var cptCode in cptCodes)
            {
                var ruleCptCode = string.IsNullOrWhiteSpace(cptCode) ? null : cptCode;

                if (lowerText.Contains("conservative therapy") && lowerText.Contains("six weeks"))
                {
                    rules.Add(new ExtractedPolicyRule(
                        "authorization_required",
                        "Conservative therapy is required before authorization.",
                        true,
                        100,
                        ruleCptCode,
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
                        ruleCptCode,
                        "neurological deficit documentation",
                        "No neurological deficit documentation"));
                }
            }
        }

        return rules;
    }
}
