namespace HealthPilot.Api.Ingestion.Policies;

public interface IPolicyNormalizer
{
    NormalizedPolicyData Normalize(PolicyIngestionRequest request, IReadOnlyList<ExtractedPolicyRule> extractedRules);
}
