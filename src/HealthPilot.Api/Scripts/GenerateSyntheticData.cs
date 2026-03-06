using HealthPilot.Api.Data;
using HealthPilot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Scripts;

public class SyntheticDataGenerator
{
    private static readonly string[] DefaultInsurerNames =
    [
        "Aetna",
        "United Health",
        "Cigna"
    ];

    public async Task GenerateAllAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        await EnsureInsurersAsync(db, cancellationToken);

        var insurers = await db.Insurers
            .Where(i => DefaultInsurerNames.Contains(i.Name))
            .Select(i => new { i.Id, i.Name })
            .ToListAsync(cancellationToken);

        var procedures = await db.Procedures
            .Where(p => p.CptCode.StartsWith("70"))
            .Select(p => new { p.Id, p.CptCode })
            .ToListAsync(cancellationToken);

        var facilities = await db.Facilities
            .OrderBy(f => f.Id)
            .Take(500)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);

        if (insurers.Count == 0 || procedures.Count == 0 || facilities.Count == 0)
        {
            return;
        }

        var insurerIds = insurers.Select(i => i.Id).ToArray();
        var procedureIds = procedures.Select(p => p.Id).ToArray();

        var existingRateKeys = (await db.NegotiatedRates
            .Where(r => insurerIds.Contains(r.InsurerId)
                        && procedureIds.Contains(r.ProcedureId)
                        && facilities.Contains(r.FacilityId))
            .Select(r => new { r.InsurerId, r.FacilityId, r.ProcedureId })
            .ToListAsync(cancellationToken))
            .Select(r => (r.InsurerId, r.FacilityId, r.ProcedureId))
            .ToHashSet();

        var now = DateTimeOffset.UtcNow;
        var random = new Random(42);

        foreach (var insurer in insurers)
        {
            foreach (var facilityId in facilities)
            {
                foreach (var procedure in procedures)
                {
                    var key = (insurer.Id, facilityId, procedure.Id);
                    if (existingRateKeys.Contains(key))
                    {
                        continue;
                    }

                    var medicareRate = GetMedicareRate(procedure.CptCode);
                    var variance = random.Next(80, 120) / 100m;

                    db.NegotiatedRates.Add(new NegotiatedRate
                    {
                        InsurerId = insurer.Id,
                        FacilityId = facilityId,
                        ProcedureId = procedure.Id,
                        Rate = Math.Round(medicareRate * variance, 2),
                        RateType = "synthetic",
                        LastUpdated = now
                    });

                    existingRateKeys.Add(key);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureInsurersAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var existingNames = await db.Insurers
            .Where(i => DefaultInsurerNames.Contains(i.Name))
            .Select(i => i.Name)
            .ToListAsync(cancellationToken);

        var insurersToAdd = DefaultInsurerNames
            .Except(existingNames, StringComparer.Ordinal)
            .Select(name => new Insurer { Name = name })
            .ToList();

        if (insurersToAdd.Count == 0)
        {
            return;
        }

        db.Insurers.AddRange(insurersToAdd);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static decimal GetMedicareRate(string cptCode)
    {
        return cptCode switch
        {
            "70553" => 1050m,
            "70551" => 960m,
            "70450" => 420m,
            "70480" => 510m,
            _ => 750m
        };
    }
}
