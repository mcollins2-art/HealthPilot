using HealthPilot.Api.Data;
using HealthPilot.Api.Ingestion.Policies;
using HealthPilot.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthPilot.Api.Tests;

public class PolicyIngestionPipelineTests
{
    [Fact]
    public async Task IngestAsync_IsIdempotentForPolicyVersion()
    {
        await using var fixture = await TestDbFixture.CreateAsync();
        fixture.DbContext.Procedures.Add(new Procedure
        {
            CptCode = "72141",
            Description = "MRI Cervical Spine",
            Category = "imaging"
        });
        await fixture.DbContext.SaveChangesAsync();

        var pipeline = new PolicyIngestionPipeline(
            new StubPolicySourceFetcher(),
            new PolicyDocumentParser(),
            new PolicyRuleExtractor(),
            new PolicyNormalizer(),
            new PolicyPersistenceService(fixture.DbContext));

        var request = new PolicyIngestionRequest(
            InsurerName: "Aetna",
            PolicyName: "Cervical MRI Policy",
            SourceUrl: "https://example.com/policy",
            VersionDate: new DateOnly(2026, 1, 1),
            EffectiveDate: new DateOnly(2026, 2, 1),
            ProcedureCptCodes: ["72141"]);

        var first = await pipeline.IngestAsync(request, CancellationToken.None);
        var second = await pipeline.IngestAsync(request, CancellationToken.None);

        Assert.True(first.RulesUpserted > 0);
        Assert.Equal(0, second.RulesUpserted);
        Assert.Equal(1, await fixture.DbContext.InsurerMedicalPolicies.CountAsync());
        Assert.Equal(1, await fixture.DbContext.PolicyVersions.CountAsync());
        Assert.Equal(first.PolicyVersionId, second.PolicyVersionId);
    }

    private sealed class StubPolicySourceFetcher : IPolicySourceFetcher
    {
        public Task<PolicySourceDocument> FetchAsync(string sourceUrl, CancellationToken cancellationToken)
        {
            const string content = "MRI of the cervical spine is medically necessary when conservative therapy has failed for at least six weeks and neurological deficit documentation is present.";
            return Task.FromResult(new PolicySourceDocument(sourceUrl, "text/html", content));
        }
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

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
        }
    }
}
