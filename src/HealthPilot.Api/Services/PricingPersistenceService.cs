using HealthPilot.Api.Data;
using HealthPilot.Api.Dtos;
using HealthPilot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Services;

public class PricingPersistenceService(
    AppDbContext dbContext,
    ILogger<PricingPersistenceService> logger) : IPricingPersistenceService
{
    public async Task<PricingPersistenceResult> UpsertPricingDataAsync(
        IReadOnlyList<StructuredPricingRecord> records,
        CancellationToken cancellationToken)
    {
        var result = new PricingPersistenceResult
        {
            RecordsReceived = records.Count
        };

        if (records.Count == 0)
        {
            logger.LogInformation("Pricing persistence received zero records.");
            return result;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var validRecords = records.Where(IsStructurallyValid).ToList();
            result.RecordsSkipped = records.Count - validRecords.Count;

            if (validRecords.Count == 0)
            {
                logger.LogWarning("All pricing records were skipped due to invalid required fields.");
                await transaction.CommitAsync(cancellationToken);
                return result;
            }

            var createdCounts = await EnsureReferenceDataAsync(validRecords, cancellationToken);
            result.ProceduresCreated = createdCounts.ProceduresCreated;
            result.FacilitiesCreated = createdCounts.FacilitiesCreated;
            result.InsurersCreated = createdCounts.InsurersCreated;

            var procedureMap = await BuildProcedureMapAsync(validRecords, cancellationToken);
            var facilityMap = await BuildFacilityMapAsync(validRecords, cancellationToken);
            var insurerMap = await BuildInsurerMapAsync(validRecords, cancellationToken);

            result.NegotiatedRatesUpserted = await UpsertNegotiatedRatesAsync(
                validRecords,
                procedureMap,
                facilityMap,
                insurerMap,
                cancellationToken);

            result.CashPricesUpserted = await UpsertCashPricesAsync(
                validRecords,
                procedureMap,
                facilityMap,
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Pricing persistence complete. Received={Received}, Skipped={Skipped}, ProceduresCreated={Procedures}, FacilitiesCreated={Facilities}, InsurersCreated={Insurers}, NegotiatedUpserted={Negotiated}, CashUpserted={Cash}",
                result.RecordsReceived,
                result.RecordsSkipped,
                result.ProceduresCreated,
                result.FacilitiesCreated,
                result.InsurersCreated,
                result.NegotiatedRatesUpserted,
                result.CashPricesUpserted);

            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(ex, "Failed to persist pricing records.");
            throw;
        }
    }

    private static bool IsStructurallyValid(StructuredPricingRecord record)
    {
        return !string.IsNullOrWhiteSpace(record.CptCode)
               && !string.IsNullOrWhiteSpace(record.ProcedureDescription)
               && !string.IsNullOrWhiteSpace(record.ProcedureCategory)
               && !string.IsNullOrWhiteSpace(record.FacilityName)
               && !string.IsNullOrWhiteSpace(record.City)
               && !string.IsNullOrWhiteSpace(record.State)
               && !string.IsNullOrWhiteSpace(record.ZipCode);
    }

    private async Task<(int ProceduresCreated, int FacilitiesCreated, int InsurersCreated)> EnsureReferenceDataAsync(
        IReadOnlyList<StructuredPricingRecord> records,
        CancellationToken cancellationToken)
    {
        var proceduresCreated = 0;
        var facilitiesCreated = 0;
        var insurersCreated = 0;

        var uniqueProcedures = records
            .GroupBy(r => NormalizeCpt(r.CptCode))
            .Select(g => g.First())
            .ToList();

        var cptCodes = uniqueProcedures.Select(r => NormalizeCpt(r.CptCode)).ToList();
        var existingProcedureCodes = await dbContext.Procedures
            .Where(p => cptCodes.Contains(p.CptCode))
            .Select(p => p.CptCode)
            .ToListAsync(cancellationToken);

        var existingProcedureSet = existingProcedureCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newProcedures = uniqueProcedures
            .Where(r => !existingProcedureSet.Contains(NormalizeCpt(r.CptCode)))
            .Select(r => new Procedure
            {
                CptCode = NormalizeCpt(r.CptCode),
                Description = r.ProcedureDescription.Trim(),
                Category = r.ProcedureCategory.Trim()
            })
            .ToList();

        if (newProcedures.Count > 0)
        {
            dbContext.Procedures.AddRange(newProcedures);
            proceduresCreated = newProcedures.Count;
        }

        var uniqueFacilityRecords = records
            .GroupBy(r => BuildFacilityKey(r.FacilityName, r.City, r.State, r.ZipCode))
            .Select(g => g.First())
            .ToList();

        var facilityNames = uniqueFacilityRecords.Select(r => r.FacilityName.Trim()).Distinct().ToList();
        var facilityZips = uniqueFacilityRecords.Select(r => r.ZipCode.Trim()).Distinct().ToList();

        var existingFacilities = await dbContext.Facilities
            .Where(f => facilityNames.Contains(f.Name) && facilityZips.Contains(f.Zip))
            .ToListAsync(cancellationToken);

        var existingFacilityKeys = existingFacilities
            .Select(f => BuildFacilityKey(f.Name, f.City, f.State, f.Zip))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newFacilities = uniqueFacilityRecords
            .Where(r => !existingFacilityKeys.Contains(BuildFacilityKey(r.FacilityName, r.City, r.State, r.ZipCode)))
            .Select(r => new Facility
            {
                Name = r.FacilityName.Trim(),
                Type = string.IsNullOrWhiteSpace(r.FacilityType) ? "Unknown" : r.FacilityType.Trim(),
                City = r.City.Trim(),
                State = r.State.Trim().ToUpperInvariant(),
                Zip = r.ZipCode.Trim()
            })
            .ToList();

        if (newFacilities.Count > 0)
        {
            dbContext.Facilities.AddRange(newFacilities);
            facilitiesCreated = newFacilities.Count;
        }

        var insurerNames = records
            .Where(r => !string.IsNullOrWhiteSpace(r.InsurerName))
            .Select(r => r.InsurerName!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (insurerNames.Count > 0)
        {
            var existingInsurers = await dbContext.Insurers
                .Where(i => insurerNames.Contains(i.Name))
                .Select(i => i.Name)
                .ToListAsync(cancellationToken);

            var existingInsurerSet = existingInsurers.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newInsurers = insurerNames
                .Where(i => !existingInsurerSet.Contains(i))
                .Select(i => new Insurer { Name = i })
                .ToList();

            if (newInsurers.Count > 0)
            {
                dbContext.Insurers.AddRange(newInsurers);
                insurersCreated = newInsurers.Count;
            }
        }

        if (proceduresCreated > 0 || facilitiesCreated > 0 || insurersCreated > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return (proceduresCreated, facilitiesCreated, insurersCreated);
    }

    private async Task<Dictionary<string, int>> BuildProcedureMapAsync(
        IReadOnlyList<StructuredPricingRecord> records,
        CancellationToken cancellationToken)
    {
        var cptCodes = records
            .Select(r => NormalizeCpt(r.CptCode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return await dbContext.Procedures
            .Where(p => cptCodes.Contains(p.CptCode))
            .ToDictionaryAsync(p => p.CptCode, p => p.Id, cancellationToken);
    }

    private async Task<Dictionary<string, int>> BuildFacilityMapAsync(
        IReadOnlyList<StructuredPricingRecord> records,
        CancellationToken cancellationToken)
    {
        var facilityNames = records.Select(r => r.FacilityName.Trim()).Distinct().ToList();
        var facilityZips = records.Select(r => r.ZipCode.Trim()).Distinct().ToList();

        var facilities = await dbContext.Facilities
            .Where(f => facilityNames.Contains(f.Name) && facilityZips.Contains(f.Zip))
            .ToListAsync(cancellationToken);

        return facilities.ToDictionary(
            f => BuildFacilityKey(f.Name, f.City, f.State, f.Zip),
            f => f.Id,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, int>> BuildInsurerMapAsync(
        IReadOnlyList<StructuredPricingRecord> records,
        CancellationToken cancellationToken)
    {
        var insurerNames = records
            .Where(r => !string.IsNullOrWhiteSpace(r.InsurerName))
            .Select(r => r.InsurerName!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (insurerNames.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        return await dbContext.Insurers
            .Where(i => insurerNames.Contains(i.Name))
            .ToDictionaryAsync(i => i.Name, i => i.Id, cancellationToken);
    }

    private async Task<int> UpsertNegotiatedRatesAsync(
        IReadOnlyList<StructuredPricingRecord> records,
        Dictionary<string, int> procedureMap,
        Dictionary<string, int> facilityMap,
        Dictionary<string, int> insurerMap,
        CancellationToken cancellationToken)
    {
        var candidateRows = new List<(int ProcedureId, int FacilityId, int InsurerId, decimal Rate, string RateType, DateTimeOffset LastUpdated)>();

        foreach (var record in records)
        {
            if (!record.NegotiatedRate.HasValue || string.IsNullOrWhiteSpace(record.InsurerName))
            {
                continue;
            }

            if (!procedureMap.TryGetValue(NormalizeCpt(record.CptCode), out var procedureId))
            {
                continue;
            }

            var facilityKey = BuildFacilityKey(record.FacilityName, record.City, record.State, record.ZipCode);
            if (!facilityMap.TryGetValue(facilityKey, out var facilityId))
            {
                continue;
            }

            var insurerName = record.InsurerName.Trim();
            if (!insurerMap.TryGetValue(insurerName, out var insurerId))
            {
                continue;
            }

            candidateRows.Add((
                procedureId,
                facilityId,
                insurerId,
                record.NegotiatedRate.Value,
                string.IsNullOrWhiteSpace(record.NegotiatedRateType) ? "contracted" : record.NegotiatedRateType.Trim(),
                record.LastUpdated));
        }

        if (candidateRows.Count == 0)
        {
            return 0;
        }

        var procedureIds = candidateRows.Select(r => r.ProcedureId).Distinct().ToList();
        var facilityIds = candidateRows.Select(r => r.FacilityId).Distinct().ToList();
        var insurerIds = candidateRows.Select(r => r.InsurerId).Distinct().ToList();

        var existingRates = await dbContext.NegotiatedRates
            .Where(r => procedureIds.Contains(r.ProcedureId)
                        && facilityIds.Contains(r.FacilityId)
                        && insurerIds.Contains(r.InsurerId))
            .ToListAsync(cancellationToken);

        var existingMap = existingRates.ToDictionary(
            r => (r.ProcedureId, r.FacilityId, r.InsurerId),
            r => r);

        foreach (var row in candidateRows)
        {
            var key = (row.ProcedureId, row.FacilityId, row.InsurerId);
            if (existingMap.TryGetValue(key, out var existingRate))
            {
                existingRate.Rate = row.Rate;
                existingRate.RateType = row.RateType;
                existingRate.LastUpdated = row.LastUpdated;
            }
            else
            {
                var newRate = new NegotiatedRate
                {
                    ProcedureId = row.ProcedureId,
                    FacilityId = row.FacilityId,
                    InsurerId = row.InsurerId,
                    Rate = row.Rate,
                    RateType = row.RateType,
                    LastUpdated = row.LastUpdated
                };

                dbContext.NegotiatedRates.Add(newRate);
                existingMap[key] = newRate;
            }
        }

        return candidateRows.Count;
    }

    private async Task<int> UpsertCashPricesAsync(
        IReadOnlyList<StructuredPricingRecord> records,
        Dictionary<string, int> procedureMap,
        Dictionary<string, int> facilityMap,
        CancellationToken cancellationToken)
    {
        var candidateRows = new List<(int ProcedureId, int FacilityId, decimal CashPrice, DateTimeOffset LastUpdated)>();

        foreach (var record in records)
        {
            if (!record.CashPrice.HasValue)
            {
                continue;
            }

            if (!procedureMap.TryGetValue(NormalizeCpt(record.CptCode), out var procedureId))
            {
                continue;
            }

            var facilityKey = BuildFacilityKey(record.FacilityName, record.City, record.State, record.ZipCode);
            if (!facilityMap.TryGetValue(facilityKey, out var facilityId))
            {
                continue;
            }

            candidateRows.Add((procedureId, facilityId, record.CashPrice.Value, record.LastUpdated));
        }

        if (candidateRows.Count == 0)
        {
            return 0;
        }

        var procedureIds = candidateRows.Select(r => r.ProcedureId).Distinct().ToList();
        var facilityIds = candidateRows.Select(r => r.FacilityId).Distinct().ToList();

        var existingPrices = await dbContext.CashPrices
            .Where(c => procedureIds.Contains(c.ProcedureId)
                        && facilityIds.Contains(c.FacilityId))
            .ToListAsync(cancellationToken);

        var existingMap = existingPrices.ToDictionary(
            c => (c.ProcedureId, c.FacilityId),
            c => c);

        foreach (var row in candidateRows)
        {
            var key = (row.ProcedureId, row.FacilityId);
            if (existingMap.TryGetValue(key, out var existingPrice))
            {
                existingPrice.CashPriceAmount = row.CashPrice;
                existingPrice.LastUpdated = row.LastUpdated;
            }
            else
            {
                var newPrice = new CashPrice
                {
                    ProcedureId = row.ProcedureId,
                    FacilityId = row.FacilityId,
                    CashPriceAmount = row.CashPrice,
                    LastUpdated = row.LastUpdated
                };

                dbContext.CashPrices.Add(newPrice);
                existingMap[key] = newPrice;
            }
        }

        return candidateRows.Count;
    }

    private static string NormalizeCpt(string cptCode) => cptCode.Trim().ToUpperInvariant();

    private static string BuildFacilityKey(string name, string city, string state, string zip)
    {
        return $"{name.Trim().ToUpperInvariant()}|{city.Trim().ToUpperInvariant()}|{state.Trim().ToUpperInvariant()}|{zip.Trim()}";
    }
}
