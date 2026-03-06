using HealthPilot.Api.Models;

namespace HealthPilot.Api.Services;

public sealed record AuthorizationPolicyBundle(
    Insurer Insurer,
    Procedure Procedure,
    PolicyVersion? ActivePolicyVersion,
    IReadOnlyList<PolicyRule> Rules,
    decimal DiagnosisProcedureRelevance);

public sealed record RuleEvaluationContext(
    string DiagnosisIcd10,
    int Age,
    string ProcedureCptCode);

public sealed record RuleEvaluationResult(
    bool AuthorizationRequired,
    IReadOnlyList<string> RequiredConditions,
    IReadOnlyList<string> MissingConditions,
    string? CommonDenialReason);

public sealed record AuthorizationProbabilityInput(
    string InsurerName,
    RuleEvaluationResult RuleEvaluation,
    decimal DiagnosisProcedureRelevance);

public sealed record AuthorizationProbabilityResult(
    decimal ApprovalProbability,
    decimal DenialRisk);
