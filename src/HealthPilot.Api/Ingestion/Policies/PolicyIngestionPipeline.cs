namespace HealthPilot.Api.Ingestion.Policies;

public class PolicyIngestionPipeline(
    IPolicySourceFetcher sourceFetcher,
    IPolicyDocumentParser documentParser,
    IPolicyRuleExtractor ruleExtractor,
    IPolicyNormalizer normalizer,
    IPolicyPersistenceService persistenceService)
{
    public async Task<PolicyIngestionResult> IngestAsync(PolicyIngestionRequest request, CancellationToken cancellationToken)
    {
        var insurer = await persistenceService.GetOrCreateInsurerAsync(request.InsurerName, cancellationToken);
        var sourceDocument = await sourceFetcher.FetchAsync(request.SourceUrl, cancellationToken);
        var text = documentParser.ParseToText(sourceDocument);
        var extractedRules = ruleExtractor.Extract(text, request.ProcedureCptCodes ?? []);
        var normalized = normalizer.Normalize(request, extractedRules);
        var rawDocumentPath = request.RawDocumentPath ?? request.SourceUrl;

        return await persistenceService.PersistAsync(
            new PersistPolicyInput(insurer, normalized, rawDocumentPath),
            cancellationToken);
    }
}
