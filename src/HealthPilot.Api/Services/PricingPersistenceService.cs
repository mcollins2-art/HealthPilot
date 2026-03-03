using HealthPilot.Api.Data;
using HealthPilot.Api.Dtos;
using HealthPilot.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace HealthPilot.Api.Services;

public class PricingPersistenceService(
    AppDbContext dbContext,
    ILogger<PricingPersistenceService> logger) : IPricingPersistenceService
{
    public async Task<PricingPersistenceResult> UpsertPricingDataAsync(
        IReadOnlyList<StructuredPricingRecord> records,
        long? ingestionJobId,
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
                ingestionJobId,
                cancellationToken);

            result.CashPricesUpserted = await UpsertCashPricesAsync(
                validRecords,
                procedureMap,
                facilityMap,
                ingestionJobId,
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
        long? ingestionJobId,
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
        if (dbContext.Database.IsNpgsql())
        {
            await BulkUpsertNegotiatedRatesPostgresAsync(candidateRows, ingestionJobId, cancellationToken);
            return candidateRows.Count;
        }

        foreach (var row in candidateRows)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO negotiated_rates ("ProcedureId", "FacilityId", "InsurerId", "Rate", "RateType", "LastUpdated", "IngestionJobId")
                VALUES ({row.ProcedureId}, {row.FacilityId}, {row.InsurerId}, {row.Rate}, {row.RateType}, {row.LastUpdated}, {ingestionJobId})
                ON CONFLICT ("ProcedureId", "FacilityId", "InsurerId")
                DO UPDATE SET
                    "Rate" = EXCLUDED."Rate",
                    "RateType" = EXCLUDED."RateType",
                    "LastUpdated" = EXCLUDED."LastUpdated",
                    "IngestionJobId" = EXCLUDED."IngestionJobId";
                """, cancellationToken);
        }

        return candidateRows.Count;
    }

    private async Task<int> UpsertCashPricesAsync(
        IReadOnlyList<StructuredPricingRecord> records,
        Dictionary<string, int> procedureMap,
        Dictionary<string, int> facilityMap,
        long? ingestionJobId,
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
        if (dbContext.Database.IsNpgsql())
        {
            await BulkUpsertCashPricesPostgresAsync(candidateRows, ingestionJobId, cancellationToken);
            return candidateRows.Count;
        }

        foreach (var row in candidateRows)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cash_prices ("ProcedureId", "FacilityId", "cash_price", "LastUpdated", "IngestionJobId")
                VALUES ({row.ProcedureId}, {row.FacilityId}, {row.CashPrice}, {row.LastUpdated}, {ingestionJobId})
                ON CONFLICT ("ProcedureId", "FacilityId")
                DO UPDATE SET
                    "cash_price" = EXCLUDED."cash_price",
                    "LastUpdated" = EXCLUDED."LastUpdated",
                    "IngestionJobId" = EXCLUDED."IngestionJobId";
                """, cancellationToken);
        }

        return candidateRows.Count;
    }

    private static string NormalizeCpt(string cptCode) => cptCode.Trim().ToUpperInvariant();

    private async Task BulkUpsertNegotiatedRatesPostgresAsync(
        List<(int ProcedureId, int FacilityId, int InsurerId, decimal Rate, string RateType, DateTimeOffset LastUpdated)> rows,
        long? ingestionJobId,
        CancellationToken cancellationToken)
    {
        var procedureIds = rows.Select(x => x.ProcedureId).ToArray();
        var facilityIds = rows.Select(x => x.FacilityId).ToArray();
        var insurerIds = rows.Select(x => x.InsurerId).ToArray();
        var rates = rows.Select(x => x.Rate).ToArray();
        var rateTypes = rows.Select(x => x.RateType).ToArray();
        var lastUpdated = rows.Select(x => x.LastUpdated.UtcDateTime).ToArray();

        if (ingestionJobId.HasValue)
        {
            var ingestionJobIds = Enumerable.Repeat(ingestionJobId.Value, rows.Count).ToArray();
            var withProvenanceSql = """
                INSERT INTO negotiated_rates ("ProcedureId", "FacilityId", "InsurerId", "Rate", "RateType", "LastUpdated", "IngestionJobId")
                SELECT * FROM UNNEST(@procedureIds::int[], @facilityIds::int[], @insurerIds::int[], @rates::numeric[], @rateTypes::text[], @lastUpdated::timestamptz[], @ingestionJobIds::bigint[])
                AS x("ProcedureId", "FacilityId", "InsurerId", "Rate", "RateType", "LastUpdated", "IngestionJobId")
                ON CONFLICT ("ProcedureId", "FacilityId", "InsurerId")
                DO UPDATE SET
                    "Rate" = EXCLUDED."Rate",
                    "RateType" = EXCLUDED."RateType",
                    "LastUpdated" = EXCLUDED."LastUpdated",
                    "IngestionJobId" = EXCLUDED."IngestionJobId";
                """;

            await dbContext.Database.ExecuteSqlRawAsync(withProvenanceSql, new object[]
            {
                new NpgsqlParameter<int[]>("procedureIds", procedureIds) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer },
                new NpgsqlParameter<int[]>("facilityIds", facilityIds) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer },
                new NpgsqlParameter<int[]>("insurerIds", insurerIds) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer },
                new NpgsqlParameter<decimal[]>("rates", rates) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Numeric },
                new NpgsqlParameter<string[]>("rateTypes", rateTypes) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text },
                new NpgsqlParameter<DateTime[]>("lastUpdated", lastUpdated) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.TimestampTz },
                new NpgsqlParameter<long[]>("ingestionJobIds", ingestionJobIds) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Bigint }
            }, cancellationToken);
            return;
        }

        var sql = """
            INSERT INTO negotiated_rates ("ProcedureId", "FacilityId", "InsurerId", "Rate", "RateType", "LastUpdated")
            SELECT * FROM UNNEST(@procedureIds::int[], @facilityIds::int[], @insurerIds::int[], @rates::numeric[], @rateTypes::text[], @lastUpdated::timestamptz[])
            AS x("ProcedureId", "FacilityId", "InsurerId", "Rate", "RateType", "LastUpdated")
            ON CONFLICT ("ProcedureId", "FacilityId", "InsurerId")
            DO UPDATE SET
                "Rate" = EXCLUDED."Rate",
                "RateType" = EXCLUDED."RateType",
                "LastUpdated" = EXCLUDED."LastUpdated";
            """;

        await dbContext.Database.ExecuteSqlRawAsync(sql, new object[]
        {
            new NpgsqlParameter<int[]>("procedureIds", procedureIds) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer },
            new NpgsqlParameter<int[]>("facilityIds", facilityIds) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer },
            new NpgsqlParameter<int[]>("insurerIds", insurerIds) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer },
            new NpgsqlParameter<decimal[]>("rates", rates) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Numeric },
            new NpgsqlParameter<string[]>("rateTypes", rateTypes) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text },
            new NpgsqlParameter<DateTime[]>("lastUpdated", lastUpdated) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.TimestampTz }
        }, cancellationToken);
    }

    private async Task BulkUpsertCashPricesPostgresAsync(
        List<(int ProcedureId, int FacilityId, decimal CashPrice, DateTimeOffset LastUpdated)> rows,
        long? ingestionJobId,
        CancellationToken cancellationToken)
    {
        var procedureIds = rows.Select(x => x.ProcedureId).ToArray();
        var facilityIds = rows.Select(x => x.FacilityId).ToArray();
        var cashPrices = rows.Select(x => x.CashPrice).ToArray();
        var lastUpdated = rows.Select(x => x.LastUpdated.UtcDateTime).ToArray();

        if (ingestionJobId.HasValue)
        {
            var ingestionJobIds = Enumerable.Repeat(ingestionJobId.Value, rows.Count).ToArray();
            var withProvenanceSql = """
                INSERT INTO cash_prices ("ProcedureId", "FacilityId", "cash_price", "LastUpdated", "IngestionJobId")
                SELECT * FROM UNNEST(@procedureIds::int[], @facilityIds::int[], @cashPrices::numeric[], @lastUpdated::timestamptz[], @ingestionJobIds::bigint[])
                AS x("ProcedureId", "FacilityId", "cash_price", "LastUpdated", "IngestionJobId")
                ON CONFLICT ("ProcedureId", "FacilityId")
                DO UPDATE SET
                    "cash_price" = EXCLUDED."cash_price",
                    "LastUpdated" = EXCLUDED."LastUpdated",
                    "IngestionJobId" = EXCLUDED."IngestionJobId";
                """;

            await dbContext.Database.ExecuteSqlRawAsync(withProvenanceSql, new object[]
            {
                new NpgsqlParameter<int[]>("procedureIds", procedureIds) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer },
                new NpgsqlParameter<int[]>("facilityIds", facilityIds) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer },
                new NpgsqlParameter<decimal[]>("cashPrices", cashPrices) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Numeric },
                new NpgsqlParameter<DateTime[]>("lastUpdated", lastUpdated) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.TimestampTz },
                new NpgsqlParameter<long[]>("ingestionJobIds", ingestionJobIds) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Bigint }
            }, cancellationToken);
            return;
        }

        var sql = """
            INSERT INTO cash_prices ("ProcedureId", "FacilityId", "cash_price", "LastUpdated")
            SELECT * FROM UNNEST(@procedureIds::int[], @facilityIds::int[], @cashPrices::numeric[], @lastUpdated::timestamptz[])
            AS x("ProcedureId", "FacilityId", "cash_price", "LastUpdated")
            ON CONFLICT ("ProcedureId", "FacilityId")
            DO UPDATE SET
                "cash_price" = EXCLUDED."cash_price",
                "LastUpdated" = EXCLUDED."LastUpdated";
            """;

        await dbContext.Database.ExecuteSqlRawAsync(sql, new object[]
        {
            new NpgsqlParameter<int[]>("procedureIds", procedureIds) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer },
            new NpgsqlParameter<int[]>("facilityIds", facilityIds) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer },
            new NpgsqlParameter<decimal[]>("cashPrices", cashPrices) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Numeric },
            new NpgsqlParameter<DateTime[]>("lastUpdated", lastUpdated) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.TimestampTz }
        }, cancellationToken);
    }

    private static string BuildFacilityKey(string name, string city, string state, string zip)
    {
        return $"{name.Trim().ToUpperInvariant()}|{city.Trim().ToUpperInvariant()}|{state.Trim().ToUpperInvariant()}|{zip.Trim()}";
    }
}
