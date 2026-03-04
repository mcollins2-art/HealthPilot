using HealthPilot.Api.Data;
using HealthPilot.Api.Dtos;
using HealthPilot.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthPilot.Api.Tests;

public class EstimateAuditServiceTests
{
    [Fact]
    public async Task LogEstimateAsync_PersistsAuditRecord_WithExpectedValues()
    {
        await using var dbContext = CreateDbContext();
        var service = new EstimateAuditService(dbContext, new NegotiatedMinPricingSelectionStrategy(), new BenefitSimulationService());

        var request = new EstimateRequest
        {
            ZipCode = "10001",
            Insurer = "Aetna",
            CptCode = "70551",
            DeductibleRemaining = 1200m,
            CoinsurancePercent = 20m,
            Copay = 50m,
            OopMaxRemaining = 3000m,
            CopayAppliesBeforeDeductible = true
        };

        var simulation = new BenefitSimulationResult(
            EstimatedPatientResponsibility: 420.25m,
            InsurerPayment: 779.75m);

        await service.LogEstimateAsync(
            request,
            simulation,
            negotiatedRateUsed: 1200m,
            traceId: "trace-audit-001",
            cancellationToken: CancellationToken.None);

        var record = await dbContext.EstimateAuditLogs.SingleAsync();

        Assert.Equal("trace-audit-001", record.TraceId);
        Assert.Equal("10001", record.ZipCode);
        Assert.Equal("Aetna", record.Insurer);
        Assert.Equal("70551", record.CptCode);
        Assert.Equal(1200m, record.NegotiatedRateUsed);
        Assert.Equal(1200m, record.DeductibleRemaining);
        Assert.Equal(20m, record.CoinsurancePercent);
        Assert.Equal(50m, record.Copay);
        Assert.Equal(3000m, record.OopMaxRemaining);
        Assert.True(record.CopayAppliesBeforeDeductible);
        Assert.Equal(420.25m, record.EstimatedPatientResponsibility);
        Assert.Equal(779.75m, record.InsurerPayment);
        Assert.Equal("1.1:v1:negotiated_min", record.BenefitLogicVersion);
        Assert.True(record.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task LogEstimateAsync_AllowsFalseCopayFlag_AndNegativeResponsibility()
    {
        await using var dbContext = CreateDbContext();
        var service = new EstimateAuditService(dbContext, new NegotiatedMinPricingSelectionStrategy(), new BenefitSimulationService());

        var request = new EstimateRequest
        {
            ZipCode = "07030",
            Insurer = "Plan B",
            CptCode = "70450",
            DeductibleRemaining = 0m,
            CoinsurancePercent = 0m,
            Copay = 0m,
            OopMaxRemaining = 0m,
            CopayAppliesBeforeDeductible = false
        };

        var simulation = new BenefitSimulationResult(
            EstimatedPatientResponsibility: -5m,
            InsurerPayment: 5m);

        await service.LogEstimateAsync(
            request,
            simulation,
            negotiatedRateUsed: 0m,
            traceId: "trace-audit-002",
            cancellationToken: CancellationToken.None);

        var record = await dbContext.EstimateAuditLogs.SingleAsync();

        Assert.False(record.CopayAppliesBeforeDeductible);
        Assert.Equal(-5m, record.EstimatedPatientResponsibility);
        Assert.Equal(5m, record.InsurerPayment);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }
}
