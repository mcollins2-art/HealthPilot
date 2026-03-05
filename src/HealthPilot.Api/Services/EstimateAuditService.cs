using HealthPilot.Api.Data;
using HealthPilot.Api.Dtos;
using HealthPilot.Api.Models;

namespace HealthPilot.Api.Services;

/// <summary>
/// Persists an immutable audit log record for each completed estimate request.
/// The log captures the full request context, the negotiated rate used, and the
/// benefit simulation output to support compliance reporting and debugging.
/// </summary>
public class EstimateAuditService(
    AppDbContext dbContext,
    IPricingSelectionStrategy pricingSelectionStrategy,
    ILogger<EstimateAuditService> logger) : IEstimateAuditService
{
    /// <inheritdoc/>
    public async Task LogEstimateAsync(
        EstimateRequest request,
        BenefitSimulationResult result,
        decimal negotiatedRateUsed,
        string traceId,
        CancellationToken cancellationToken)
    {
        var auditLog = new EstimateAuditLog
        {
            CreatedAt = DateTimeOffset.UtcNow,
            TraceId = traceId,
            ZipCode = request.ZipCode,
            Insurer = request.Insurer,
            CptCode = request.CptCode,
            NegotiatedRateUsed = negotiatedRateUsed,
            DeductibleRemaining = request.DeductibleRemaining,
            CoinsurancePercent = request.CoinsurancePercent,
            Copay = request.Copay,
            OopMaxRemaining = request.OopMaxRemaining,
            CopayAppliesBeforeDeductible = request.CopayAppliesBeforeDeductible,
            EstimatedPatientResponsibility = result.EstimatedPatientResponsibility,
            InsurerPayment = result.InsurerPayment,
            BenefitLogicVersion = $"{MonetaryPolicy.PolicyVersion}:{pricingSelectionStrategy.PolicyName}"
        };

        dbContext.EstimateAuditLogs.Add(auditLog);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Estimate audit logged. TraceId={TraceId}, ZipCode={ZipCode}, Insurer={Insurer}, CptCode={CptCode}, NegotiatedRateUsed={NegotiatedRateUsed}, PatientResponsibility={PatientResponsibility}",
            traceId,
            request.ZipCode,
            request.Insurer,
            request.CptCode,
            negotiatedRateUsed,
            result.EstimatedPatientResponsibility);
    }
}
