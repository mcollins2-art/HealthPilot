using HealthPilot.Api.Dtos;

namespace HealthPilot.Api.Services;

public class AuthorizationDecisionService(
    IAuthorizationPolicyService policyService,
    IAuthorizationRuleEvaluator ruleEvaluator,
    IAuthorizationProbabilityModel probabilityModel) : IAuthorizationDecisionService
{
    public async Task<AuthorizationEstimateResponse> EstimateAsync(
        AuthorizationEstimateRequest request,
        CancellationToken cancellationToken)
    {
        var bundle = await policyService.GetPolicyBundleAsync(
            request.Insurer,
            request.ProcedureCpt,
            request.DiagnosisIcd10,
            cancellationToken);

        if (bundle is null)
        {
            return new AuthorizationEstimateResponse
            {
                AuthorizationRequired = false,
                ApprovalProbability = 0.75m,
                RequiredConditions = [],
                CommonDenialReason = null
            };
        }

        var evaluation = ruleEvaluator.Evaluate(
            bundle.Rules,
            new RuleEvaluationContext(request.DiagnosisIcd10, request.Age, request.ProcedureCpt));

        var probability = probabilityModel.Estimate(new AuthorizationProbabilityInput(
            bundle.Insurer.Name,
            evaluation,
            bundle.DiagnosisProcedureRelevance));

        return new AuthorizationEstimateResponse
        {
            AuthorizationRequired = evaluation.AuthorizationRequired,
            ApprovalProbability = probability.ApprovalProbability,
            RequiredConditions = [.. evaluation.RequiredConditions],
            CommonDenialReason = evaluation.CommonDenialReason
        };
    }
}
