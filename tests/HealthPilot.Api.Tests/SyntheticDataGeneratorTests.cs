using HealthPilot.Api.Data;
using HealthPilot.Api.Models;
using HealthPilot.Api.Scripts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthPilot.Api.Tests;

public class SyntheticDataGeneratorTests
{
    [Fact]
    public async Task GenerateAllAsync_CreatesDefaultInsurers_AndNegotiatedRates()
    {
        await using var fixture = await TestDbFixture.CreateAsync();
        await fixture.SeedFacilitiesAsync(2, "10001");
        await fixture.SeedProceduresAsync("70553", "70480");

        var generator = new SyntheticDataGenerator();

        await generator.GenerateAllAsync(fixture.DbContext);

        var insurerNames = await fixture.DbContext.Insurers
            .OrderBy(i => i.Name)
            .Select(i => i.Name)
            .ToListAsync();

        Assert.Equal(["Aetna", "Cigna", "United Health"], insurerNames);

        var rates = await fixture.DbContext.NegotiatedRates.ToListAsync();
        Assert.Equal(12, rates.Count);
        Assert.All(rates, r =>
        {
            Assert.True(r.Rate > 0m);
            Assert.Equal("synthetic", r.RateType);
        });
    }

    [Fact]
    public async Task GenerateAllAsync_IsIdempotent_WhenRunTwice()
    {
        await using var fixture = await TestDbFixture.CreateAsync();
        await fixture.SeedFacilitiesAsync(1, "10001");
        await fixture.SeedProceduresAsync("70553");

        var generator = new SyntheticDataGenerator();

        await generator.GenerateAllAsync(fixture.DbContext);
        var firstRunCount = await fixture.DbContext.NegotiatedRates.CountAsync();

        await generator.GenerateAllAsync(fixture.DbContext);
        var secondRunCount = await fixture.DbContext.NegotiatedRates.CountAsync();

        Assert.Equal(3, firstRunCount);
        Assert.Equal(firstRunCount, secondRunCount);
    }

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

        public async Task SeedFacilitiesAsync(int count, string zip)
        {
            for (var i = 0; i < count; i++)
            {
                DbContext.Facilities.Add(new Facility
                {
                    Name = $"Facility {i + 1}",
                    Type = "hospital",
                    City = "New York",
                    State = "NY",
                    Zip = zip
                });
            }

            await DbContext.SaveChangesAsync();
        }

        public async Task SeedProceduresAsync(params string[] cptCodes)
        {
            foreach (var cptCode in cptCodes)
            {
                DbContext.Procedures.Add(new Procedure
                {
                    CptCode = cptCode,
                    Description = $"Procedure {cptCode}",
                    Category = "imaging"
                });
            }

            await DbContext.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
        }
    }
}
