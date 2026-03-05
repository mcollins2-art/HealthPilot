using HealthPilot.Api.Dtos;

namespace HealthPilot.Api.Services;

/// <summary>
/// Writes an immutable audit record for each estimate request, capturing the full input and
/// output of benefit simulation for compliance and debugging purposes.
/// </summary>
public interface IEstimateAuditService
{
    /// <summary>
    /// Persists an audit log entry for a completed estimate request.
    /// </summary>
    /// <param name="request">The original estimate request submitted by the caller.</param>
    /// <param name="result">The benefit simulation result produced for this request.</param>
    /// <param name="negotiatedRateUsed">The representative negotiated rate that drove the simulation.</param>
    /// <param name="traceId">The HTTP request trace identifier for correlation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogEstimateAsync(
        EstimateRequest request,
        BenefitSimulationResult result,
        decimal negotiatedRateUsed,
        string traceId,
        CancellationToken cancellationToken);
}