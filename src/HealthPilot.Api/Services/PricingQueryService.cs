using HealthPilot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Services;

public class PricingQueryService(AppDbContext dbContext) : IPricingQueryService
{
    public async Task<PricingSummary> GetPricingSummaryAsync(
        string zipCode,
        string insurer,
        string cptCode,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        string normalizedZip = zipCode.Trim();
        string normalizedInsurer = insurer.Trim();
        string normalizedCpt = cptCode.Trim().ToUpperInvariant();
        string normalizedTenant = tenantId ?? string.Empty;

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
            .Where(f => f.TenantId == normalizedTenant)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);

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

    public string FormatRange(decimal? minValue, decimal? maxValue)
    {
        if (!minValue.HasValue || !maxValue.HasValue)
        {
            return "N/A";
        }

        return $"${minValue.Value:F2} - ${maxValue.Value:F2}";
    }
}
