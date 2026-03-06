namespace HealthPilot.Api.Services;

public class AuthorizationProbabilityModel : IAuthorizationProbabilityModel
{
    private const decimal BaseProbability = 0.75m;
    private const decimal ConservativeTherapyPenalty = 0.25m;
    private const decimal DiagnosisMatchBoost = 0.10m;
    private const decimal StrictInsurerPenalty = 0.10m;
    private const decimal MinimumProbability = 0.05m;
    private const decimal MaximumProbability = 0.95m;
    private static readonly HashSet<string> StrictInsurers = new(StringComparer.OrdinalIgnoreCase)
    {
        "UnitedHealthcare",
        "Cigna"
    };

    public AuthorizationProbabilityResult Estimate(AuthorizationProbabilityInput input)
    {
        var probability = BaseProbability;

        if (input.RuleEvaluation.MissingConditions.Any(x => x.Contains("conservative therapy", StringComparison.OrdinalIgnoreCase)))
        {
            probability -= ConservativeTherapyPenalty;
        }

        if (input.DiagnosisProcedureRelevance >= 0.70m)
        {
            probability += DiagnosisMatchBoost;
        }

        if (IsStrictInsurer(input.InsurerName))
        {
            probability -= StrictInsurerPenalty;
        }

        probability = decimal.Clamp(probability, MinimumProbability, MaximumProbability);
        return new AuthorizationProbabilityResult(probability, 1m - probability);
    }

    private static bool IsStrictInsurer(string insurerName)
    {
        return StrictInsurers.Contains(insurerName);
    }
}
