using HealthPilot.Api.Data;
using HealthPilot.Api.Models;
using HealthPilot.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HealthPilot.Api.Tests;

public class PricingQueryServiceTests
{
    [Fact]
    public async Task GetPricingSummaryAsync_ReturnsNullRanges_WhenProcedureMissing()
    {
        await using var fixture = await TestDbFixture.CreateAsync();
        await SeedFacilityAsync(fixture, "10001");

        var service = new PricingQueryService(fixture.Factory, new MemoryCache(new MemoryCacheOptions()), NullLogger<PricingQueryService>.Instance);
        var summary = await service.GetPricingSummaryAsync("10001", "Aetna", "70551", CancellationToken.None);

        Assert.Null(summary.NegotiatedMin);
        Assert.Null(summary.NegotiatedMax);
        Assert.Null(summary.CashMin);
        Assert.Null(summary.CashMax);
    }

    [Fact]
    public async Task GetPricingSummaryAsync_ReturnsNullRanges_WhenNoFacilitiesInZip()
    {
        await using var fixture = await TestDbFixture.CreateAsync();

        await using var db = fixture.Factory.CreateDbContext();
        db.Procedures.Add(new Procedure { CptCode = "70551", Description = "Brain MRI", Category = "imaging" });
        await db.SaveChangesAsync();

        var service = new PricingQueryService(fixture.Factory, new MemoryCache(new MemoryCacheOptions()), NullLogger<PricingQueryService>.Instance);
        var summary = await service.GetPricingSummaryAsync("99999", "Aetna", "70551", CancellationToken.None);

        Assert.Null(summary.NegotiatedMin);
        Assert.Null(summary.NegotiatedMax);
        Assert.Null(summary.CashMin);
        Assert.Null(summary.CashMax);
    }

    [Fact]
    public async Task GetPricingSummaryAsync_ReturnsCashOnly_WhenInsurerNotFound()
    {
        await using var fixture = await TestDbFixture.CreateAsync();

        var facilityA = await SeedFacilityAsync(fixture, "10001", "Hospital A");
        var facilityB = await SeedFacilityAsync(fixture, "10001", "Hospital B");

        await using var db = fixture.Factory.CreateDbContext();
        var procedure = new Procedure { CptCode = "70551", Description = "Brain MRI", Category = "imaging" };
        db.Procedures.Add(procedure);
        await db.SaveChangesAsync();

        db.CashPrices.AddRange(
            new CashPrice { ProcedureId = procedure.Id, FacilityId = facilityA.Id, CashPriceAmount = 900m, LastUpdated = DateTimeOffset.UtcNow },
            new CashPrice { ProcedureId = procedure.Id, FacilityId = facilityB.Id, CashPriceAmount = 1200m, LastUpdated = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var service = new PricingQueryService(fixture.Factory, new MemoryCache(new MemoryCacheOptions()), NullLogger<PricingQueryService>.Instance);
        var summary = await service.GetPricingSummaryAsync("10001", "Unknown Insurer", "70551", CancellationToken.None);

        Assert.Null(summary.NegotiatedMin);
        Assert.Null(summary.NegotiatedMax);
        Assert.Equal(900m, summary.CashMin);
        Assert.Equal(1200m, summary.CashMax);
    }

    [Fact]
    public async Task GetPricingSummaryAsync_ReturnsNegotiatedAndCashRanges_WhenInsurerAndProcedureMatch()
    {
        await using var fixture = await TestDbFixture.CreateAsync();

        var facilityA = await SeedFacilityAsync(fixture, "10001", "Hospital A");
        var facilityB = await SeedFacilityAsync(fixture, "10001", "Hospital B");

        await using var db = fixture.Factory.CreateDbContext();
        var procedure = new Procedure { CptCode = "70551", Description = "Brain MRI", Category = "imaging" };
        var insurer = new Insurer { Name = "Aetna" };
        db.Procedures.Add(procedure);
        db.Insurers.Add(insurer);
        await db.SaveChangesAsync();

        db.CashPrices.AddRange(
            new CashPrice { ProcedureId = procedure.Id, FacilityId = facilityA.Id, CashPriceAmount = 800m, LastUpdated = DateTimeOffset.UtcNow },
            new CashPrice { ProcedureId = procedure.Id, FacilityId = facilityB.Id, CashPriceAmount = 1300m, LastUpdated = DateTimeOffset.UtcNow });

        db.NegotiatedRates.AddRange(
            new NegotiatedRate { ProcedureId = procedure.Id, FacilityId = facilityA.Id, InsurerId = insurer.Id, Rate = 950m, RateType = "contracted", LastUpdated = DateTimeOffset.UtcNow },
            new NegotiatedRate { ProcedureId = procedure.Id, FacilityId = facilityB.Id, InsurerId = insurer.Id, Rate = 1100m, RateType = "contracted", LastUpdated = DateTimeOffset.UtcNow });

        await db.SaveChangesAsync();

        var service = new PricingQueryService(fixture.Factory, new MemoryCache(new MemoryCacheOptions()), NullLogger<PricingQueryService>.Instance);
        var summary = await service.GetPricingSummaryAsync(" 10001 ", " Aetna ", "70551", CancellationToken.None);

        Assert.Equal(950m, summary.NegotiatedMin);
        Assert.Equal(1100m, summary.NegotiatedMax);
        Assert.Equal(800m, summary.CashMin);
        Assert.Equal(1300m, summary.CashMax);
    }

    [Fact]
    public async Task GetPricingSummaryAsync_ReturnsCachedResult_OnSecondCall()
    {
        await using var fixture = await TestDbFixture.CreateAsync();

        await using var db = fixture.Factory.CreateDbContext();
        var procedure = new Procedure { CptCode = "70551", Description = "Brain MRI", Category = "imaging" };
        db.Procedures.Add(procedure);
        await db.SaveChangesAsync();

        var facility = await SeedFacilityAsync(fixture, "10001");
        db.CashPrices.Add(new CashPrice { ProcedureId = procedure.Id, FacilityId = facility.Id, CashPriceAmount = 500m, LastUpdated = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new PricingQueryService(fixture.Factory, cache, NullLogger<PricingQueryService>.Instance);

        var first = await service.GetPricingSummaryAsync("10001", "Unknown", "70551", CancellationToken.None);
        Assert.Equal(500m, first.CashMin);

        // Add a second facility price to the DB and save it — the cache should
        // still serve the original result without hitting the DB again.
        var facility2 = await SeedFacilityAsync(fixture, "10001", "Hospital B");
        await using var db2 = fixture.Factory.CreateDbContext();
        db2.CashPrices.Add(new CashPrice { ProcedureId = procedure.Id, FacilityId = facility2.Id, CashPriceAmount = 999m, LastUpdated = DateTimeOffset.UtcNow });
        await db2.SaveChangesAsync();

        var second = await service.GetPricingSummaryAsync("10001", "Unknown", "70551", CancellationToken.None);
        Assert.Equal(first.CashMin, second.CashMin);
    }

    [Fact]
    public async Task FormatRange_ReturnsExpectedOutput()
    {
        await using var fixture = await TestDbFixture.CreateAsync();
        var service = new PricingQueryService(fixture.Factory, new MemoryCache(new MemoryCacheOptions()), NullLogger<PricingQueryService>.Instance);

        Assert.Equal("N/A", service.FormatRange(null, 100m));
        Assert.Equal("N/A", service.FormatRange(100m, null));
        Assert.Equal("$100.00 - $150.25", service.FormatRange(100m, 150.25m));
    }

    private static async Task<Facility> SeedFacilityAsync(TestDbFixture fixture, string zip, string name = "General Hospital")
    {
        await using var db = fixture.Factory.CreateDbContext();
        var facility = new Facility
        {
            Name = name,
            Type = "hospital",
            City = "New York",
            State = "NY",
            Zip = zip
        };

        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        return facility;
    }

    private sealed class TestDbFixture : IAsyncDisposable
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public IDbContextFactory<AppDbContext> Factory { get; }

        private TestDbFixture(DbContextOptions<AppDbContext> options)
        {
            _options = options;
            Factory = new SimpleDbContextFactory(options);
        }

        public static Task<TestDbFixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;

            return Task.FromResult(new TestDbFixture(options));
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = new AppDbContext(_options);
            await db.Database.EnsureDeletedAsync();
        }

        private sealed class SimpleDbContextFactory(DbContextOptions<AppDbContext> options)
            : IDbContextFactory<AppDbContext>
        {
            public AppDbContext CreateDbContext() => new(options);
        }
    }
}

