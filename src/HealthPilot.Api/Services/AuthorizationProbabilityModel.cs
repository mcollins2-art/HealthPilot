namespace HealthPilot.Api.Services;

public class AuthorizationProbabilityModel : IAuthorizationProbabilityModel
{
    public AuthorizationProbabilityResult Estimate(AuthorizationProbabilityInput input)
    {
        var probability = 0.75m;

        if (input.RuleEvaluation.MissingConditions.Any(x => x.Contains("conservative therapy", StringComparison.OrdinalIgnoreCase)))
        {
            probability -= 0.25m;
        }

        if (input.DiagnosisProcedureRelevance >= 0.70m)
        {
            probability += 0.10m;
        }

        if (IsStrictInsurer(input.InsurerName))
        {
            probability -= 0.10m;
        }

        probability = decimal.Clamp(probability, 0.05m, 0.95m);
        return new AuthorizationProbabilityResult(probability, 1m - probability);
    }

    private static bool IsStrictInsurer(string insurerName)
    {
        return insurerName.Equals("UnitedHealthcare", StringComparison.OrdinalIgnoreCase);
    }
}
