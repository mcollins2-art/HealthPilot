namespace HealthPilot.Api.Services;

public interface IAuthorizationProbabilityModel
{
    AuthorizationProbabilityResult Estimate(AuthorizationProbabilityInput input);
}
