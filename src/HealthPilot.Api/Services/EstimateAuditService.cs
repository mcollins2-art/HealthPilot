using HealthPilot.Api.Dtos;

namespace HealthPilot.Api.Services;

/// <summary>
/// Enqueues an immutable audit snapshot for each completed estimate request into the
/// <see cref="BackgroundAuditChannel"/>. The actual database write is performed
/// off the HTTP request critical path by <see cref="BackgroundAuditWriter"/>.
/// </summary>
public class EstimateAuditService(
    BackgroundAuditChannel auditChannel,
    IPricingSelectionStrategy pricingSelectionStrategy,
    ILogger<EstimateAuditService> logger) : IEstimateAuditService
{
    /// <inheritdoc/>
    public Task LogEstimateAsync(
        EstimateRequest request,
        BenefitSimulationResult result,
        decimal negotiatedRateUsed,
        string traceId,
        CancellationToken cancellationToken)
    {
        var item = new AuditItem(
            CreatedAt: DateTimeOffset.UtcNow,
            TraceId: traceId,
            ZipCode: request.ZipCode,
            Insurer: request.Insurer,
            CptCode: request.CptCode,
            NegotiatedRateUsed: negotiatedRateUsed,
            DeductibleRemaining: request.DeductibleRemaining,
            CoinsurancePercent: request.CoinsurancePercent,
            Copay: request.Copay,
            OopMaxRemaining: request.OopMaxRemaining,
            CopayAppliesBeforeDeductible: request.CopayAppliesBeforeDeductible,
            EstimatedPatientResponsibility: result.EstimatedPatientResponsibility,
            InsurerPayment: result.InsurerPayment,
            BenefitLogicVersion: $"{MonetaryPolicy.PolicyVersion}:{pricingSelectionStrategy.PolicyName}");

        if (!auditChannel.Writer.TryWrite(item))
        {
            logger.LogWarning(
                "Audit channel rejected write for TraceId={TraceId}. The audit log entry will be lost.",
                traceId);
        }

        return Task.CompletedTask;
    }
}

