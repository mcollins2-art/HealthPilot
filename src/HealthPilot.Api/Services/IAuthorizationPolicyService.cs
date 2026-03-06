namespace HealthPilot.Api.Services;

public interface IAuthorizationPolicyService
{
    Task<AuthorizationPolicyBundle?> GetPolicyBundleAsync(
        string insurerName,
        string procedureCptCode,
        string diagnosisIcd10,
        CancellationToken cancellationToken);
}
