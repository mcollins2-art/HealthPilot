using HealthPilot.Api.Models;

namespace HealthPilot.Api.Services;

public interface IAuthorizationRuleEvaluator
{
    RuleEvaluationResult Evaluate(IReadOnlyList<PolicyRule> rules, RuleEvaluationContext context);
}
