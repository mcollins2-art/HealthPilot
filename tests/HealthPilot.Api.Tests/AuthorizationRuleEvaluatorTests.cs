using HealthPilot.Api.Models;
using HealthPilot.Api.Services;
using Xunit;

namespace HealthPilot.Api.Tests;

public class AuthorizationRuleEvaluatorTests
{
    [Fact]
    public void Evaluate_ReturnsAuthorizationRequiredAndMissingConditions()
    {
        var evaluator = new AuthorizationRuleEvaluator();
        var rules = new List<PolicyRule>
        {
            new()
            {
                RuleType = "authorization_required",
                Description = "Prior authorization required.",
                Required = true,
                Priority = 10,
                ConditionExpression = "6 weeks conservative therapy",
                DenialReason = "No documented conservative treatment"
            },
            new()
            {
                RuleType = "clinical_criteria",
                Description = "Neurological deficit documentation required.",
                Required = true,
                Priority = 20,
                ConditionExpression = "neurological deficit documentation"
            }
        };

        var result = evaluator.Evaluate(rules, new RuleEvaluationContext("M54.2", 45, "72141"));

        Assert.True(result.AuthorizationRequired);
        Assert.Equal(2, result.RequiredConditions.Count);
        Assert.Equal(2, result.MissingConditions.Count);
        Assert.Equal("No documented conservative treatment", result.CommonDenialReason);
    }

    [Fact]
    public void Evaluate_SatisfiesAgeThresholdRule()
    {
        var evaluator = new AuthorizationRuleEvaluator();
        var rules = new List<PolicyRule>
        {
            new()
            {
                RuleType = "clinical_criteria",
                Description = "Adult criteria",
                Required = true,
                Priority = 1,
                ConditionExpression = "age >= 18"
            }
        };

        var result = evaluator.Evaluate(rules, new RuleEvaluationContext("M54.2", 40, "72141"));

        Assert.Empty(result.MissingConditions);
        Assert.Null(result.CommonDenialReason);
    }
}
