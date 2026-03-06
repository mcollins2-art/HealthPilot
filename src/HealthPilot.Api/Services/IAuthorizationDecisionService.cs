using HealthPilot.Api.Dtos;

namespace HealthPilot.Api.Services;

public interface IAuthorizationDecisionService
{
    Task<AuthorizationEstimateResponse> EstimateAsync(
        AuthorizationEstimateRequest request,
        CancellationToken cancellationToken);
}
