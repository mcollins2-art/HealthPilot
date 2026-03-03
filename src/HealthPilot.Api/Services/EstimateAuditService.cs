using HealthPilot.Api.Data;
using HealthPilot.Api.Dtos;
using HealthPilot.Api.Models;

namespace HealthPilot.Api.Services;

public class EstimateAuditService(AppDbContext dbContext) : IEstimateAuditService
{
    private const string CurrentBenefitLogicVersion = "1.0";

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
            BenefitLogicVersion = CurrentBenefitLogicVersion
        };

        dbContext.EstimateAuditLogs.Add(auditLog);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}