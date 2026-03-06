namespace HealthPilot.Api.Ingestion.Policies;

public class PolicyNormalizer : IPolicyNormalizer
{
    public NormalizedPolicyData Normalize(PolicyIngestionRequest request, IReadOnlyList<ExtractedPolicyRule> extractedRules)
    {
        var cptCodes = (request.ProcedureCptCodes ?? [])
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var normalizedRules = extractedRules
            .Select(x => x with
            {
                Description = x.Description.Trim(),
                ConditionExpression = x.ConditionExpression?.Trim(),
                ProcedureCptCode = x.ProcedureCptCode?.Trim()
            })
            .ToList();

        return new NormalizedPolicyData(request, normalizedRules, cptCodes);
    }
}
