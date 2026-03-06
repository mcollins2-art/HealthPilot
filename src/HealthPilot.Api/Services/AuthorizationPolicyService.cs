using HealthPilot.Api.Data;
using HealthPilot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Services;

public class AuthorizationPolicyService(AppDbContext dbContext) : IAuthorizationPolicyService
{
    public async Task<AuthorizationPolicyBundle?> GetPolicyBundleAsync(
        string insurerName,
        string procedureCptCode,
        string diagnosisIcd10,
        CancellationToken cancellationToken)
    {
        var normalizedInsurer = insurerName.Trim();
        var normalizedProcedureCpt = procedureCptCode.Trim();
        var normalizedDiagnosis = diagnosisIcd10.Trim().ToUpperInvariant();

        var insurer = await dbContext.Insurers
            .SingleOrDefaultAsync(
                x => x.Name.ToLower() == normalizedInsurer.ToLower(),
                cancellationToken);

        if (insurer is null)
        {
            return null;
        }

        var procedure = await dbContext.Procedures
            .SingleOrDefaultAsync(x => x.CptCode == normalizedProcedureCpt, cancellationToken);

        if (procedure is null)
        {
            return null;
        }

        var activePolicyVersion = await dbContext.PolicyVersions
            .AsNoTracking()
            .Where(x =>
                x.Policy!.InsurerId == insurer.Id &&
                x.EffectiveDate <= DateOnly.FromDateTime(DateTime.UtcNow))
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.VersionDate)
            .FirstOrDefaultAsync(cancellationToken);

        IReadOnlyList<PolicyRule> rules = [];
        if (activePolicyVersion is not null)
        {
            rules = await dbContext.PolicyRules
                .AsNoTracking()
                .Where(x =>
                    x.PolicyVersionId == activePolicyVersion.Id &&
                    (x.ProcedureCptCode == null || x.ProcedureCptCode == normalizedProcedureCpt))
                .OrderBy(x => x.Priority)
                .ToListAsync(cancellationToken);
        }

        var relevance = await dbContext.DiagnosisProcedureMappings
            .AsNoTracking()
            .Where(x => x.Icd10Code == normalizedDiagnosis && x.CptCode == normalizedProcedureCpt)
            .Select(x => (decimal?)x.RelevanceScore)
            .FirstOrDefaultAsync(cancellationToken) ?? 0m;

        return new AuthorizationPolicyBundle(
            insurer,
            procedure,
            activePolicyVersion,
            rules,
            relevance);
    }
}
