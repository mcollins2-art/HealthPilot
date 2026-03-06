using HealthPilot.Api.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Services;

public class PricingQueryService(AppDbContext dbContext, IMemoryCache memoryCache) : IPricingQueryService
{
    private static readonly TimeSpan LookupCacheTtl = TimeSpan.FromMinutes(5);

    public async Task<PricingSummary> GetPricingSummaryAsync(
        string zipCode,
        string insurer,
        string cptCode,
        CancellationToken cancellationToken)
    {
        var (normalizedZip, normalizedInsurer, normalizedCpt) = NormalizeInputs(zipCode, insurer, cptCode);

        // Resolve dimension keys once, then run fact-table queries on integer IDs.
        int? procedureId = await GetProcedureIdAsync(normalizedCpt, cancellationToken);
        int? insurerId = await GetInsurerIdAsync(normalizedInsurer, cancellationToken);
        var facilityIds = await GetFacilityIdsByZipAsync(normalizedZip, cancellationToken);

        if (!procedureId.HasValue || facilityIds.Count == 0)
        {
            return new PricingSummary(null, null, null, null);
        }

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

        return new PricingSummary(negotiatedMin, negotiatedMax, cashMin, cashMax);
    }

    public async Task<CheapestProviderSummary?> GetCheapestProviderAsync(
        string zipCode,
        string insurer,
        string cptCode,
        CancellationToken cancellationToken)
    {
        var (normalizedZip, normalizedInsurer, normalizedCpt) = NormalizeInputs(zipCode, insurer, cptCode);
        int? procedureId = await GetProcedureIdAsync(normalizedCpt, cancellationToken);
        if (!procedureId.HasValue)
        {
            return null;
        }

        int? insurerId = await GetInsurerIdAsync(normalizedInsurer, cancellationToken);
        if (insurerId.HasValue)
        {
            var negotiatedCandidate = await dbContext.NegotiatedRates
                .AsNoTracking()
                .Where(n => n.ProcedureId == procedureId.Value
                            && n.InsurerId == insurerId.Value
                            && n.Facility.Zip == normalizedZip)
                .OrderBy(n => n.Rate)
                .ThenBy(n => n.Facility.Name)
                .Select(n => new CheapestProviderSummary(
                    n.Facility.Name,
                    n.Facility.City,
                    n.Facility.State,
                    n.Facility.Zip,
                    n.Rate,
                    null,
                    n.Rate))
                .FirstOrDefaultAsync(cancellationToken);

            if (negotiatedCandidate is not null)
            {
                return negotiatedCandidate;
            }
        }

        return await dbContext.CashPrices
            .AsNoTracking()
            .Where(c => c.ProcedureId == procedureId.Value && c.Facility.Zip == normalizedZip)
            .OrderBy(c => c.CashPriceAmount)
            .ThenBy(c => c.Facility.Name)
            .Select(c => new CheapestProviderSummary(
                c.Facility.Name,
                c.Facility.City,
                c.Facility.State,
                c.Facility.Zip,
                null,
                c.CashPriceAmount,
                c.CashPriceAmount))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public string FormatRange(decimal? minValue, decimal? maxValue)
    {
        if (!minValue.HasValue || !maxValue.HasValue)
        {
            return "N/A";
        }

        return $"${minValue.Value:F2} - ${maxValue.Value:F2}";
    }

    private static (string Zip, string Insurer, string Cpt) NormalizeInputs(string zipCode, string insurer, string cptCode) =>
        (zipCode.Trim(), insurer.Trim(), cptCode.Trim().ToUpperInvariant());

    private async Task<int?> GetProcedureIdAsync(string normalizedCpt, CancellationToken cancellationToken)
    {
        return await memoryCache.GetOrCreateAsync($"procedure:{normalizedCpt}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = LookupCacheTtl;
            return await dbContext.Procedures
                .AsNoTracking()
                .Where(p => p.CptCode == normalizedCpt)
                .Select(p => (int?)p.Id)
                .FirstOrDefaultAsync(cancellationToken);
        });
    }

    private async Task<int?> GetInsurerIdAsync(string normalizedInsurer, CancellationToken cancellationToken)
    {
        return await memoryCache.GetOrCreateAsync($"insurer:{normalizedInsurer}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = LookupCacheTtl;
            return await dbContext.Insurers
                .AsNoTracking()
                .Where(i => i.Name == normalizedInsurer)
                .Select(i => (int?)i.Id)
                .FirstOrDefaultAsync(cancellationToken);
        });
    }

    private async Task<List<int>> GetFacilityIdsByZipAsync(string normalizedZip, CancellationToken cancellationToken)
    {
        return await memoryCache.GetOrCreateAsync($"facilities:{normalizedZip}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = LookupCacheTtl;
            return await dbContext.Facilities
                .AsNoTracking()
                .Where(f => f.Zip == normalizedZip)
                .Select(f => f.Id)
                .ToListAsync(cancellationToken);
        }) ?? [];
    }
}
