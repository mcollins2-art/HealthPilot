using HealthPilot.Api.Data;
using HealthPilot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Services;

/// <summary>
/// Hosted background service that drains the <see cref="BackgroundAuditChannel"/> and
/// persists each <see cref="AuditItem"/> to the database using a dedicated scope.
/// Moving the DB write off the HTTP request critical path eliminates the synchronous
/// audit-write latency seen by end users.
/// </summary>
public sealed class BackgroundAuditWriter(
    BackgroundAuditChannel auditChannel,
    IDbContextFactory<AppDbContext> dbContextFactory,
    ILogger<BackgroundAuditWriter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (AuditItem item in auditChannel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using AppDbContext db = await dbContextFactory.CreateDbContextAsync(stoppingToken);
                db.EstimateAuditLogs.Add(new EstimateAuditLog
                {
                    CreatedAt = item.CreatedAt,
                    TraceId = item.TraceId,
                    ZipCode = item.ZipCode,
                    Insurer = item.Insurer,
                    CptCode = item.CptCode,
                    NegotiatedRateUsed = item.NegotiatedRateUsed,
                    DeductibleRemaining = item.DeductibleRemaining,
                    CoinsurancePercent = item.CoinsurancePercent,
                    Copay = item.Copay,
                    OopMaxRemaining = item.OopMaxRemaining,
                    CopayAppliesBeforeDeductible = item.CopayAppliesBeforeDeductible,
                    EstimatedPatientResponsibility = item.EstimatedPatientResponsibility,
                    InsurerPayment = item.InsurerPayment,
                    BenefitLogicVersion = item.BenefitLogicVersion
                });
                await db.SaveChangesAsync(stoppingToken);

                logger.LogInformation(
                    "Estimate audit written. TraceId={TraceId}, ZipCode={ZipCode}, Insurer={Insurer}, CptCode={CptCode}, NegotiatedRateUsed={NegotiatedRateUsed}, PatientResponsibility={PatientResponsibility}",
                    item.TraceId,
                    item.ZipCode,
                    item.Insurer,
                    item.CptCode,
                    item.NegotiatedRateUsed,
                    item.EstimatedPatientResponsibility);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown — stop processing.
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to persist audit log. TraceId={TraceId}, ZipCode={ZipCode}, Insurer={Insurer}, CptCode={CptCode}",
                    item.TraceId, item.ZipCode, item.Insurer, item.CptCode);
            }
        }
    }
}
