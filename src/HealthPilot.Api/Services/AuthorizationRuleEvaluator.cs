using System.Text.RegularExpressions;
using HealthPilot.Api.Models;

namespace HealthPilot.Api.Services;

public class AuthorizationRuleEvaluator : IAuthorizationRuleEvaluator
{
    private static readonly Regex AgeThresholdPattern = new(@"age\s*(>=|>|<=|<)\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ConservativeTherapyPattern = new(@"conservative therapy.*(\d+)\s*weeks?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public RuleEvaluationResult Evaluate(IReadOnlyList<PolicyRule> rules, RuleEvaluationContext context)
    {
        var requiredConditions = new List<string>();
        var missingConditions = new List<string>();
        string? commonDenialReason = null;
        var authorizationRequired = false;

        foreach (var rule in rules)
        {
            if (rule.RuleType.Equals("authorization_required", StringComparison.OrdinalIgnoreCase) && rule.Required)
            {
                authorizationRequired = true;
            }

            if (!rule.Required)
            {
                continue;
            }

            var condition = NormalizeCondition(rule.ConditionExpression ?? rule.Description);
            if (string.IsNullOrWhiteSpace(condition))
            {
                continue;
            }

            requiredConditions.Add(condition);

            if (IsSatisfied(condition, context))
            {
                continue;
            }

            missingConditions.Add(condition);
            commonDenialReason ??= rule.DenialReason ?? BuildDenialReason(condition);
        }

        return new RuleEvaluationResult(
            authorizationRequired,
            requiredConditions,
            missingConditions,
            commonDenialReason);
    }

    private static bool IsSatisfied(string condition, RuleEvaluationContext context)
    {
        if (condition.Contains("conservative therapy", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (condition.Contains("neurological deficit", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var ageThreshold = AgeThresholdPattern.Match(condition);
        if (ageThreshold.Success && int.TryParse(ageThreshold.Groups[2].Value, out var age))
        {
            var op = ageThreshold.Groups[1].Value;
            return op switch
            {
                ">=" => context.Age >= age,
                ">" => context.Age > age,
                "<=" => context.Age <= age,
                "<" => context.Age < age,
                _ => false
            };
        }

        var conservativeTherapyWeeks = ConservativeTherapyPattern.Match(condition);
        if (conservativeTherapyWeeks.Success)
        {
            return false;
        }

        if (condition.Contains("icd10:", StringComparison.OrdinalIgnoreCase))
        {
            var requiredDiagnosis = condition["icd10:".Length..].Trim();
            return string.Equals(requiredDiagnosis, context.DiagnosisIcd10, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static string NormalizeCondition(string condition)
    {
        return condition.Trim().TrimEnd('.');
    }

    private static string BuildDenialReason(string condition)
    {
        return $"No documented {condition.ToLowerInvariant()}";
    }
}
