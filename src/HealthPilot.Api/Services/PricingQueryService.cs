using HealthPilot.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HealthPilot.Api.Services;

public class PricingQueryService(AppDbContext dbContext, ILogger<PricingQueryService> logger) : IPricingQueryService
{
    private static readonly ActivitySource ActivitySource = new("HealthPilot.PricingQueryService");
    private static readonly Meter Meter = new("HealthPilot.Pricing");
    private static readonly Histogram<double> QueryDurationMs = Meter.CreateHistogram<double>("pricing_query_duration_ms");
    private static readonly Histogram<int> FacilityCount = Meter.CreateHistogram<int>("pricing_query_facility_count");
    private static readonly Counter<long> CacheHitCounter = Meter.CreateCounter<long>("pricing_cache_hits");
    private static readonly Counter<long> CacheMissCounter = Meter.CreateCounter<long>("pricing_cache_misses");

    public async Task<PricingSummary> GetPricingSummaryAsync(
        string zipCode,
        string insurer,
        string cptCode,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("GetPricingSummary");
        var startedAt = Stopwatch.GetTimestamp();
        string normalizedZip = zipCode.Trim();
        string normalizedInsurer = insurer.Trim();
        string normalizedCpt = cptCode.Trim().ToUpperInvariant();

        activity?.SetTag("pricing.zip_code", normalizedZip);
        activity?.SetTag("pricing.insurer", normalizedInsurer);
        activity?.SetTag("pricing.cpt_code", normalizedCpt);

        // Resolve dimension keys once, then run fact-table queries on integer IDs.
        int? procedureId = await dbContext.Procedures
            .AsNoTracking()
            .Where(p => p.CptCode == normalizedCpt)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        int? insurerId = await dbContext.Insurers
            .AsNoTracking()
            .Where(i => i.Name == normalizedInsurer)
            .Select(i => (int?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var facilityIds = await dbContext.Facilities
            .AsNoTracking()
            .Where(f => f.Zip == normalizedZip)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);
        FacilityCount.Record(facilityIds.Count);
        activity?.SetTag("pricing.facility_count", facilityIds.Count);

        if (!procedureId.HasValue || facilityIds.Count == 0)
        {
            CacheMissCounter.Add(1);
            activity?.SetTag("pricing.cache_hit", false);
            QueryDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            logger.LogWarning(
                "No pricing found for Zip={ZipCode}, Insurer={Insurer}, CptCode={CptCode}",
                normalizedZip,
                normalizedInsurer,
                normalizedCpt);
            return new PricingSummary(null, null, null, null, null);
        }

        var cashQuery = dbContext.CashPrices
            .AsNoTracking()
            .Where(c => c.ProcedureId == procedureId.Value && facilityIds.Contains(c.FacilityId))
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Min = (decimal?)group.Min(x => x.CashPriceAmount),
                Max = (decimal?)group.Max(x => x.CashPriceAmount),
                LastUpdated = (DateTimeOffset?)group.Max(x => x.LastUpdated)
            });

        var cashStats = await cashQuery.FirstOrDefaultAsync(cancellationToken);
        var cashMin = cashStats?.Min;
        var cashMax = cashStats?.Max;
        var cashLastUpdated = cashStats?.LastUpdated;

        decimal? negotiatedMin = null;
        decimal? negotiatedMax = null;
        DateTimeOffset? negotiatedLastUpdated = null;

        if (insurerId.HasValue)
        {
            var negotiatedQuery = dbContext.NegotiatedRates
                .AsNoTracking()
                .Where(r => r.ProcedureId == procedureId.Value
                            && r.InsurerId == insurerId.Value
                            && facilityIds.Contains(r.FacilityId))
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Min = (decimal?)group.Min(x => x.Rate),
                    Max = (decimal?)group.Max(x => x.Rate),
                    LastUpdated = (DateTimeOffset?)group.Max(x => x.LastUpdated)
                });

            var negotiatedStats = await negotiatedQuery.FirstOrDefaultAsync(cancellationToken);
            negotiatedMin = negotiatedStats?.Min;
            negotiatedMax = negotiatedStats?.Max;
            negotiatedLastUpdated = negotiatedStats?.LastUpdated;
        }

        var pricingLastUpdated = MaxDate(negotiatedLastUpdated, cashLastUpdated);
        var hasPricing = negotiatedMin.HasValue || negotiatedMax.HasValue || cashMin.HasValue || cashMax.HasValue;
        if (hasPricing)
        {
            CacheHitCounter.Add(1);
        }
        else
        {
            CacheMissCounter.Add(1);
            logger.LogWarning(
                "No pricing found for Zip={ZipCode}, Insurer={Insurer}, CptCode={CptCode}",
                normalizedZip,
                normalizedInsurer,
                normalizedCpt);
        }

        activity?.SetTag("pricing.cache_hit", hasPricing);
        QueryDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        return new PricingSummary(negotiatedMin, negotiatedMax, cashMin, cashMax, pricingLastUpdated);
    }

    public string FormatRange(decimal? minValue, decimal? maxValue)
    {
        if (!minValue.HasValue || !maxValue.HasValue)
        {
            return "N/A";
        }

        return $"${minValue.Value:F2} - ${maxValue.Value:F2}";
    }

    private static DateTimeOffset? MaxDate(DateTimeOffset? a, DateTimeOffset? b)
    {
        if (a is null)
        {
            return b;
        }

        if (b is null)
        {
            return a;
        }

        return a > b ? a : b;
    }
}
