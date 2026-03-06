using HealthPilot.Api.Models;

namespace HealthPilot.Api.Ingestion.Policies;

public sealed record PolicyIngestionRequest(
    string InsurerName,
    string PolicyName,
    string SourceUrl,
    DateOnly VersionDate,
    DateOnly EffectiveDate,
    string? RawDocumentPath = null,
    IReadOnlyList<string>? ProcedureCptCodes = null);

public sealed record PolicySourceDocument(
    string SourceUrl,
    string ContentType,
    string Content);

public sealed record ExtractedPolicyRule(
    string RuleType,
    string Description,
    bool Required,
    int Priority,
    string? ProcedureCptCode,
    string? ConditionExpression,
    string? DenialReason);

public sealed record NormalizedPolicyData(
    PolicyIngestionRequest Request,
    IReadOnlyList<ExtractedPolicyRule> Rules,
    IReadOnlyList<string> ProcedureCptCodes);

public sealed record PolicyIngestionResult(
    int PolicyId,
    int PolicyVersionId,
    int RulesUpserted,
    int ProcedureMappingsUpserted);

public sealed record PersistPolicyInput(
    Insurer Insurer,
    NormalizedPolicyData NormalizedData,
    string RawDocumentPath);
