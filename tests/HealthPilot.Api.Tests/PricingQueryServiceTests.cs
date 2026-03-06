using HealthPilot.Api.Data;
using HealthPilot.Api.Models;
using HealthPilot.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace HealthPilot.Api.Tests;

public class PricingQueryServiceTests
{
    [Fact]
    public async Task GetPricingSummaryAsync_ReturnsNullRanges_WhenProcedureMissing()
    {
        await using var fixture = await TestDbFixture.CreateAsync();
        await SeedFacilityAsync(fixture.DbContext, "10001");

        var service = CreateService(fixture.DbContext);
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

        var procedure = new Procedure { CptCode = "70551", Description = "Brain MRI", Category = "imaging" };
        fixture.DbContext.Procedures.Add(procedure);
        await fixture.DbContext.SaveChangesAsync();

        var service = CreateService(fixture.DbContext);
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

        var procedure = new Procedure { CptCode = "70551", Description = "Brain MRI", Category = "imaging" };
        var facilityA = await SeedFacilityAsync(fixture.DbContext, "10001", "Hospital A");
        var facilityB = await SeedFacilityAsync(fixture.DbContext, "10001", "Hospital B");

        fixture.DbContext.Procedures.Add(procedure);
        await fixture.DbContext.SaveChangesAsync();

        fixture.DbContext.CashPrices.AddRange(
            new CashPrice { ProcedureId = procedure.Id, FacilityId = facilityA.Id, CashPriceAmount = 900m, LastUpdated = DateTimeOffset.UtcNow },
            new CashPrice { ProcedureId = procedure.Id, FacilityId = facilityB.Id, CashPriceAmount = 1200m, LastUpdated = DateTimeOffset.UtcNow });
        await fixture.DbContext.SaveChangesAsync();

        var service = CreateService(fixture.DbContext);
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

        var procedure = new Procedure { CptCode = "70551", Description = "Brain MRI", Category = "imaging" };
        var insurer = new Insurer { Name = "Aetna" };
        var facilityA = await SeedFacilityAsync(fixture.DbContext, "10001", "Hospital A");
        var facilityB = await SeedFacilityAsync(fixture.DbContext, "10001", "Hospital B");

        fixture.DbContext.Procedures.Add(procedure);
        fixture.DbContext.Insurers.Add(insurer);
        await fixture.DbContext.SaveChangesAsync();

        fixture.DbContext.CashPrices.AddRange(
            new CashPrice { ProcedureId = procedure.Id, FacilityId = facilityA.Id, CashPriceAmount = 800m, LastUpdated = DateTimeOffset.UtcNow },
            new CashPrice { ProcedureId = procedure.Id, FacilityId = facilityB.Id, CashPriceAmount = 1300m, LastUpdated = DateTimeOffset.UtcNow });

        fixture.DbContext.NegotiatedRates.AddRange(
            new NegotiatedRate { ProcedureId = procedure.Id, FacilityId = facilityA.Id, InsurerId = insurer.Id, Rate = 950m, RateType = "contracted", LastUpdated = DateTimeOffset.UtcNow },
            new NegotiatedRate { ProcedureId = procedure.Id, FacilityId = facilityB.Id, InsurerId = insurer.Id, Rate = 1100m, RateType = "contracted", LastUpdated = DateTimeOffset.UtcNow });

        await fixture.DbContext.SaveChangesAsync();

        var service = CreateService(fixture.DbContext);
        var summary = await service.GetPricingSummaryAsync(" 10001 ", " Aetna ", "70551", CancellationToken.None);

        Assert.Equal(950m, summary.NegotiatedMin);
        Assert.Equal(1100m, summary.NegotiatedMax);
        Assert.Equal(800m, summary.CashMin);
        Assert.Equal(1300m, summary.CashMax);
    }

    [Fact]
    public async Task FormatRange_ReturnsExpectedOutput()
    {
        await using var fixture = await TestDbFixture.CreateAsync();
        var service = CreateService(fixture.DbContext);

        Assert.Equal("N/A", service.FormatRange(null, 100m));
        Assert.Equal("N/A", service.FormatRange(100m, null));
        Assert.Equal("$100.00 - $150.25", service.FormatRange(100m, 150.25m));
    }

    [Fact]
    public async Task GetCheapestProviderAsync_ReturnsLowestNegotiatedRate_WhenInsurerExists()
    {
        await using var fixture = await TestDbFixture.CreateAsync();

        var procedure = new Procedure { CptCode = "70551", Description = "Brain MRI", Category = "imaging" };
        var insurer = new Insurer { Name = "Aetna" };
        var facilityA = await SeedFacilityAsync(fixture.DbContext, "10001", "Hospital A");
        var facilityB = await SeedFacilityAsync(fixture.DbContext, "10001", "Hospital B");
        fixture.DbContext.Procedures.Add(procedure);
        fixture.DbContext.Insurers.Add(insurer);
        await fixture.DbContext.SaveChangesAsync();

        fixture.DbContext.NegotiatedRates.AddRange(
            new NegotiatedRate { ProcedureId = procedure.Id, FacilityId = facilityA.Id, InsurerId = insurer.Id, Rate = 950m, RateType = "contracted", LastUpdated = DateTimeOffset.UtcNow },
            new NegotiatedRate { ProcedureId = procedure.Id, FacilityId = facilityB.Id, InsurerId = insurer.Id, Rate = 900m, RateType = "contracted", LastUpdated = DateTimeOffset.UtcNow });
        await fixture.DbContext.SaveChangesAsync();

        var service = CreateService(fixture.DbContext);
        var provider = await service.GetCheapestProviderAsync("10001", "Aetna", "70551", CancellationToken.None);

        Assert.NotNull(provider);
        Assert.Equal("Hospital B", provider!.ProviderName);
        Assert.Equal(900m, provider.SelectedPrice);
        Assert.Equal(900m, provider.NegotiatedRate);
    }

    [Fact]
    public async Task GetCheapestProviderAsync_FallsBackToCashPrice_WhenInsurerMissing()
    {
        await using var fixture = await TestDbFixture.CreateAsync();

        var procedure = new Procedure { CptCode = "70551", Description = "Brain MRI", Category = "imaging" };
        var facilityA = await SeedFacilityAsync(fixture.DbContext, "10001", "Hospital A");
        var facilityB = await SeedFacilityAsync(fixture.DbContext, "10001", "Hospital B");
        fixture.DbContext.Procedures.Add(procedure);
        await fixture.DbContext.SaveChangesAsync();

        fixture.DbContext.CashPrices.AddRange(
            new CashPrice { ProcedureId = procedure.Id, FacilityId = facilityA.Id, CashPriceAmount = 825m, LastUpdated = DateTimeOffset.UtcNow },
            new CashPrice { ProcedureId = procedure.Id, FacilityId = facilityB.Id, CashPriceAmount = 790m, LastUpdated = DateTimeOffset.UtcNow });
        await fixture.DbContext.SaveChangesAsync();

        var service = CreateService(fixture.DbContext);
        var provider = await service.GetCheapestProviderAsync("10001", "Unknown", "70551", CancellationToken.None);

        Assert.NotNull(provider);
        Assert.Equal("Hospital B", provider!.ProviderName);
        Assert.Equal(790m, provider.SelectedPrice);
        Assert.Equal(790m, provider.CashPrice);
    }

    private static async Task<Facility> SeedFacilityAsync(AppDbContext dbContext, string zip, string name = "General Hospital")
    {
        var facility = new Facility
        {
            Name = name,
            Type = "hospital",
            City = "New York",
            State = "NY",
            Zip = zip
        };

        dbContext.Facilities.Add(facility);
        await dbContext.SaveChangesAsync();
        return facility;
    }

    private static PricingQueryService CreateService(AppDbContext dbContext) =>
        new(dbContext, new MemoryCache(new MemoryCacheOptions()));

    private sealed class TestDbFixture : IAsyncDisposable
    {
        public AppDbContext DbContext { get; }

        private TestDbFixture(AppDbContext dbContext)
        {
            DbContext = dbContext;
        }

        public static async Task<TestDbFixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;

            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            return new TestDbFixture(dbContext);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
        }
    }
}
