using HealthPilot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Services;

public class PricingQueryService(AppDbContext dbContext) : IPricingQueryService
{
    public async Task<PricingSummary> GetPricingSummaryAsync(
        string zipCode,
        string insurer,
        string cptCode,
        CancellationToken cancellationToken)
    {
        string normalizedZip = zipCode.Trim();
        string normalizedInsurer = insurer.Trim();
        string normalizedCpt = cptCode.Trim().ToUpperInvariant();

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

        if (!procedureId.HasValue || facilityIds.Count == 0)
        {
            return new PricingSummary(null, null, null, null, facilityIds.Count, null, null);
        }

        var cashRows = await dbContext.CashPrices
            .AsNoTracking()
            .Where(c => c.ProcedureId == procedureId.Value && facilityIds.Contains(c.FacilityId))
            .Select(c => new { Amount = (decimal?)c.CashPriceAmount, c.LastUpdated })
            .ToListAsync(cancellationToken);

        var cashMin = cashRows.Count == 0 ? null : cashRows.Min(c => c.Amount);
        var cashMax = cashRows.Count == 0 ? null : cashRows.Max(c => c.Amount);
        var dataAsOfDate = cashRows.Count == 0 ? (DateTimeOffset?)null : cashRows.Max(c => c.LastUpdated);

        decimal? negotiatedMin = null;
        decimal? negotiatedMax = null;
        string? matchedInsurer = null;

        if (insurerId.HasValue)
        {
            var negotiatedRows = await dbContext.NegotiatedRates
                .AsNoTracking()
                .Where(r => r.ProcedureId == procedureId.Value
                            && r.InsurerId == insurerId.Value
                            && facilityIds.Contains(r.FacilityId))
                .Select(r => new { Amount = (decimal?)r.Rate, r.PolicyVersion, r.LastUpdated })
                .ToListAsync(cancellationToken);

            if (negotiatedRows.Count > 0)
            {
                negotiatedMin = negotiatedRows.Min(r => r.Amount);
                negotiatedMax = negotiatedRows.Max(r => r.Amount);
                var latestNegotiatedUpdate = negotiatedRows.Max(r => r.LastUpdated);
                if (!dataAsOfDate.HasValue || latestNegotiatedUpdate > dataAsOfDate.Value)
                {
                    dataAsOfDate = latestNegotiatedUpdate;
                }

                var latestPolicyVersion = negotiatedRows
                    .OrderByDescending(r => r.LastUpdated)
                    .Select(r => r.PolicyVersion)
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                matchedInsurer = string.IsNullOrWhiteSpace(latestPolicyVersion)
                    ? normalizedInsurer
                    : $"{normalizedInsurer} ({latestPolicyVersion})";
            }
        }

        return new PricingSummary(
            negotiatedMin,
            negotiatedMax,
            cashMin,
            cashMax,
            facilityIds.Count,
            matchedInsurer,
            dataAsOfDate);
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
