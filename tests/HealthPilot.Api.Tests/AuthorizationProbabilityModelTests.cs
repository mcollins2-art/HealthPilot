using HealthPilot.Api.Services;
using Xunit;

namespace HealthPilot.Api.Tests;

public class AuthorizationProbabilityModelTests
{
    [Fact]
    public void Estimate_AppliesHeuristicsAndClamp()
    {
        var model = new AuthorizationProbabilityModel();
        var input = new AuthorizationProbabilityInput(
            "UnitedHealthcare",
            new RuleEvaluationResult(
                AuthorizationRequired: true,
                RequiredConditions: ["6 weeks conservative therapy"],
                MissingConditions: ["6 weeks conservative therapy"],
                CommonDenialReason: "No documented conservative treatment"),
            DiagnosisProcedureRelevance: 0.95m);

        var result = model.Estimate(input);

        Assert.Equal(0.50m, result.ApprovalProbability);
        Assert.Equal(0.50m, result.DenialRisk);
    }

    [Fact]
    public void Estimate_ClampsLowerBound()
    {
        var model = new AuthorizationProbabilityModel();
        var input = new AuthorizationProbabilityInput(
            "UnitedHealthcare",
            new RuleEvaluationResult(
                AuthorizationRequired: true,
                RequiredConditions: ["6 weeks conservative therapy", "neurological deficit documentation"],
                MissingConditions: ["6 weeks conservative therapy", "neurological deficit documentation"],
                CommonDenialReason: "No documented conservative treatment"),
            DiagnosisProcedureRelevance: 0m);

        var result = model.Estimate(input);

        Assert.Equal(0.40m, result.ApprovalProbability);
    }
}
