using HealthPilot.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HealthPilot.Api.Services;

/// <summary>
/// Queries the normalized pricing tables to retrieve negotiated-rate and cash-price aggregates
/// for a given zip code, insurer, and CPT procedure code.
/// </summary>
/// <remarks>
/// The three dimension-key lookups (procedure, insurer, facility) are executed in parallel
/// using separate <see cref="AppDbContext"/> instances obtained from the pooled factory.
/// Results are cached in <see cref="IMemoryCache"/> for one minute to avoid redundant DB
/// round-trips for identical (zip, insurer, CPT) combinations.
/// </remarks>
public class PricingQueryService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    IMemoryCache cache,
    ILogger<PricingQueryService> logger) : IPricingQueryService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(1);

    /// <inheritdoc/>
    public async Task<PricingSummary> GetPricingSummaryAsync(
        string zipCode,
        string insurer,
        string cptCode,
        CancellationToken cancellationToken)
    {
        string normalizedZip = zipCode.Trim();
        string normalizedInsurer = insurer.Trim();
        string normalizedCpt = cptCode.Trim().ToUpperInvariant();

        string cacheKey = $"pricing:{normalizedZip}:{normalizedInsurer}:{normalizedCpt}";

        if (cache.TryGetValue(cacheKey, out PricingSummary? cached))
        {
            return cached!;
        }

        // Resolve dimension keys in parallel — each query runs on its own context
        // instance so there are no concurrent-operation violations on a single DbContext.
        await using AppDbContext procCtx = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using AppDbContext insurerCtx = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using AppDbContext facilityCtx = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        Task<int?> procedureTask = procCtx.Procedures
            .AsNoTracking()
            .Where(p => p.CptCode == normalizedCpt)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        Task<int?> insurerTask = insurerCtx.Insurers
            .AsNoTracking()
            .Where(i => i.Name == normalizedInsurer)
            .Select(i => (int?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);

        Task<List<int>> facilityTask = facilityCtx.Facilities
            .AsNoTracking()
            .Where(f => f.Zip == normalizedZip)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);

        await Task.WhenAll(procedureTask, insurerTask, facilityTask);

        int? procedureId = await procedureTask;
        int? insurerId = await insurerTask;
        List<int> facilityIds = await facilityTask;

        if (!procedureId.HasValue || facilityIds.Count == 0)
        {
            logger.LogDebug(
                "No pricing data found for ZipCode={ZipCode}, CptCode={CptCode}: ProcedureFound={ProcedureFound}, FacilityCount={FacilityCount}",
                normalizedZip, normalizedCpt, procedureId.HasValue, facilityIds.Count);

            var empty = new PricingSummary(null, null, null, null);
            cache.Set(cacheKey, empty, CacheTtl);
            return empty;
        }

        // Run remaining aggregate queries on a single context sequentially.
        await using AppDbContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var cashQuery = dbContext.CashPrices
            .AsNoTracking()
            .Where(c => c.ProcedureId == procedureId.Value && facilityIds.Contains(c.FacilityId))
            .Select(c => (decimal?)c.CashPriceAmount);

        var cashMin = await cashQuery.DefaultIfEmpty().MinAsync(cancellationToken);
        var cashMax = await cashQuery.DefaultIfEmpty().MaxAsync(cancellationToken);

        decimal? negotiatedMin = null;
        decimal? negotiatedMax = null;

        if (insurerId.HasValue)
        {
            var negotiatedQuery = dbContext.NegotiatedRates
                .AsNoTracking()
                .Where(r => r.ProcedureId == procedureId.Value
                            && r.InsurerId == insurerId.Value
                            && facilityIds.Contains(r.FacilityId))
                .Select(r => (decimal?)r.Rate);

            negotiatedMin = await negotiatedQuery.DefaultIfEmpty().MinAsync(cancellationToken);
            negotiatedMax = await negotiatedQuery.DefaultIfEmpty().MaxAsync(cancellationToken);
        }
        else
        {
            logger.LogDebug(
                "Insurer '{Insurer}' not found in database; negotiated rates will be null.",
                normalizedInsurer);
        }

        logger.LogDebug(
            "Pricing lookup complete for ZipCode={ZipCode}, Insurer={Insurer}, CptCode={CptCode}. NegotiatedMin={NegotiatedMin}, NegotiatedMax={NegotiatedMax}, CashMin={CashMin}, CashMax={CashMax}",
            normalizedZip, normalizedInsurer, normalizedCpt, negotiatedMin, negotiatedMax, cashMin, cashMax);

        var result = new PricingSummary(negotiatedMin, negotiatedMax, cashMin, cashMax);
        cache.Set(cacheKey, result, CacheTtl);
        return result;
    }

    /// <inheritdoc/>
    public string FormatRange(decimal? minValue, decimal? maxValue)
    {
        if (!minValue.HasValue || !maxValue.HasValue)
        {
            return "N/A";
        }

        return $"${minValue.Value:F2} - ${maxValue.Value:F2}";
    }
}

