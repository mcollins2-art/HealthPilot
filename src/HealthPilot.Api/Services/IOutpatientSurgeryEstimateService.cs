using HealthPilot.Api.Dtos;

namespace HealthPilot.Api.Services;

public interface IOutpatientSurgeryEstimateService
{
    Task<OutpatientSurgeryEstimateResponse> EstimateAsync(
        OutpatientSurgeryEstimateRequest request,
        CancellationToken cancellationToken);
}
