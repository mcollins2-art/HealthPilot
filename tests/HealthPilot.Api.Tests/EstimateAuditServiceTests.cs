using HealthPilot.Api.Data;
using HealthPilot.Api.Dtos;
using HealthPilot.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HealthPilot.Api.Tests;

public class EstimateAuditServiceTests
{
    [Fact]
    public async Task LogEstimateAsync_EnqueuesAuditItem_WithExpectedValues()
    {
        var channel = new BackgroundAuditChannel();
        var service = new EstimateAuditService(channel, new NegotiatedMinPricingSelectionStrategy(), NullLogger<EstimateAuditService>.Instance);

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

        Assert.True(channel.Reader.TryRead(out AuditItem? item));
        Assert.NotNull(item);
        Assert.Equal("trace-audit-001", item.TraceId);
        Assert.Equal("10001", item.ZipCode);
        Assert.Equal("Aetna", item.Insurer);
        Assert.Equal("70551", item.CptCode);
        Assert.Equal(1200m, item.NegotiatedRateUsed);
        Assert.Equal(1200m, item.DeductibleRemaining);
        Assert.Equal(20m, item.CoinsurancePercent);
        Assert.Equal(50m, item.Copay);
        Assert.Equal(3000m, item.OopMaxRemaining);
        Assert.True(item.CopayAppliesBeforeDeductible);
        Assert.Equal(420.25m, item.EstimatedPatientResponsibility);
        Assert.Equal(779.75m, item.InsurerPayment);
        Assert.Equal("1.1:negotiated_min", item.BenefitLogicVersion);
        Assert.True(item.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task LogEstimateAsync_AllowsFalseCopayFlag_AndNegativeResponsibility()
    {
        var channel = new BackgroundAuditChannel();
        var service = new EstimateAuditService(channel, new NegotiatedMinPricingSelectionStrategy(), NullLogger<EstimateAuditService>.Instance);

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

        Assert.True(channel.Reader.TryRead(out AuditItem? item));
        Assert.NotNull(item);
        Assert.False(item.CopayAppliesBeforeDeductible);
        Assert.Equal(-5m, item.EstimatedPatientResponsibility);
        Assert.Equal(5m, item.InsurerPayment);
    }
}

public class BackgroundAuditWriterTests
{
    [Fact]
    public async Task ExecuteAsync_WritesAuditItemToDatabase()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var factory = new SimpleDbContextFactory(options);

        var channel = new BackgroundAuditChannel();
        var writer = new BackgroundAuditWriter(channel, factory, NullLogger<BackgroundAuditWriter>.Instance);

        var item = new AuditItem(
            CreatedAt: DateTimeOffset.UtcNow,
            TraceId: "trace-writer-001",
            ZipCode: "10001",
            Insurer: "Aetna",
            CptCode: "70551",
            NegotiatedRateUsed: 1000m,
            DeductibleRemaining: 500m,
            CoinsurancePercent: 20m,
            Copay: 30m,
            OopMaxRemaining: 2000m,
            CopayAppliesBeforeDeductible: true,
            EstimatedPatientResponsibility: 250m,
            InsurerPayment: 750m,
            BenefitLogicVersion: "1.1:negotiated_min");

        await channel.Writer.WriteAsync(item);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var writerTask = writer.StartAsync(cts.Token);

        // Wait until the record appears or we time out.
        await using var db = factory.CreateDbContext();
        for (int i = 0; i < 50; i++)
        {
            if (await db.EstimateAuditLogs.AnyAsync(CancellationToken.None))
            {
                break;
            }

            await Task.Delay(100, CancellationToken.None);
        }

        cts.Cancel();
        await writer.StopAsync(CancellationToken.None);

        var record = await db.EstimateAuditLogs.SingleAsync(CancellationToken.None);
        Assert.Equal("trace-writer-001", record.TraceId);
        Assert.Equal("10001", record.ZipCode);
        Assert.Equal("Aetna", record.Insurer);
        Assert.Equal("70551", record.CptCode);
        Assert.Equal(1000m, record.NegotiatedRateUsed);
        Assert.Equal(500m, record.DeductibleRemaining);
        Assert.Equal(20m, record.CoinsurancePercent);
        Assert.Equal(30m, record.Copay);
        Assert.Equal(2000m, record.OopMaxRemaining);
        Assert.True(record.CopayAppliesBeforeDeductible);
        Assert.Equal(250m, record.EstimatedPatientResponsibility);
        Assert.Equal(750m, record.InsurerPayment);
        Assert.Equal("1.1:negotiated_min", record.BenefitLogicVersion);
    }

    private sealed class SimpleDbContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}

