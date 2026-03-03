using HealthPilot.Api.Dtos;

namespace HealthPilot.Api.Services;

public interface IEstimateAuditService
{
    Task LogEstimateAsync(
        EstimateRequest request,
        BenefitSimulationResult result,
        decimal negotiatedRateUsed,
        string traceId,
        CancellationToken cancellationToken);
}