using HealthPilot.Api.Data;
using HealthPilot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Ingestion.Policies;

public class PolicyPersistenceService(AppDbContext dbContext) : IPolicyPersistenceService
{
    public async Task<Insurer> GetOrCreateInsurerAsync(string insurerName, CancellationToken cancellationToken)
    {
        var normalizedInsurerName = insurerName.Trim();

        var insurer = await dbContext.Insurers
            .SingleOrDefaultAsync(x => x.Name.ToLower() == normalizedInsurerName.ToLower(), cancellationToken);

        if (insurer is not null)
        {
            return insurer;
        }

        insurer = new Insurer { Name = normalizedInsurerName };
        dbContext.Insurers.Add(insurer);
        await dbContext.SaveChangesAsync(cancellationToken);
        return insurer;
    }

    public async Task<PolicyIngestionResult> PersistAsync(PersistPolicyInput input, CancellationToken cancellationToken)
    {
        var request = input.NormalizedData.Request;
        var policy = await dbContext.InsurerMedicalPolicies
            .SingleOrDefaultAsync(
                x => x.InsurerId == input.Insurer.Id && x.PolicyName == request.PolicyName,
                cancellationToken);

        if (policy is null)
        {
            policy = new InsurerMedicalPolicy
            {
                InsurerId = input.Insurer.Id,
                PolicyName = request.PolicyName.Trim(),
                SourceUrl = request.SourceUrl.Trim()
            };
            dbContext.InsurerMedicalPolicies.Add(policy);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var policyVersion = await dbContext.PolicyVersions
            .SingleOrDefaultAsync(
                x => x.PolicyId == policy.Id && x.VersionDate == request.VersionDate,
                cancellationToken);

        if (policyVersion is null)
        {
            policyVersion = new PolicyVersion
            {
                PolicyId = policy.Id,
                VersionDate = request.VersionDate,
                EffectiveDate = request.EffectiveDate,
                RawDocumentPath = input.RawDocumentPath
            };
            dbContext.PolicyVersions.Add(policyVersion);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var existingRules = await dbContext.PolicyRules
            .Where(x => x.PolicyVersionId == policyVersion.Id)
            .ToListAsync(cancellationToken);

        var addedRules = 0;
        foreach (var rule in input.NormalizedData.Rules)
        {
            var exists = existingRules.Any(x =>
                x.RuleType == rule.RuleType &&
                x.Description == rule.Description &&
                x.Required == rule.Required &&
                x.Priority == rule.Priority &&
                x.ProcedureCptCode == rule.ProcedureCptCode &&
                x.ConditionExpression == rule.ConditionExpression);

            if (exists)
            {
                continue;
            }

            dbContext.PolicyRules.Add(new PolicyRule
            {
                PolicyVersionId = policyVersion.Id,
                RuleType = rule.RuleType,
                Description = rule.Description,
                Required = rule.Required,
                Priority = rule.Priority,
                ProcedureCptCode = rule.ProcedureCptCode,
                ConditionExpression = rule.ConditionExpression,
                DenialReason = rule.DenialReason
            });

            addedRules++;
        }

        var mappedProcedures = 0;
        foreach (var cptCode in input.NormalizedData.ProcedureCptCodes)
        {
            var procedure = await dbContext.Procedures.SingleOrDefaultAsync(x => x.CptCode == cptCode, cancellationToken);
            if (procedure is null)
            {
                continue;
            }

            var mappingExists = await dbContext.PolicyProcedureMappings.AnyAsync(
                x => x.PolicyVersionId == policyVersion.Id && x.ProcedureId == procedure.Id,
                cancellationToken);

            if (mappingExists)
            {
                continue;
            }

            dbContext.PolicyProcedureMappings.Add(new PolicyProcedureMapping
            {
                PolicyVersionId = policyVersion.Id,
                ProcedureId = procedure.Id
            });
            mappedProcedures++;
        }

        if (addedRules > 0 || mappedProcedures > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new PolicyIngestionResult(policy.Id, policyVersion.Id, addedRules, mappedProcedures);
    }
}
