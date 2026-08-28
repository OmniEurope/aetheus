// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Analysis;

internal static class AnalysisPolicyRequestValidator
{
    public static void Validate(UpsertAnalysisPolicyRequest request)
    {
        var metricRule = !string.IsNullOrWhiteSpace(request.MetricKey);
        var findingRule = request.SeverityThreshold.HasValue || !string.IsNullOrWhiteSpace(request.RuleId)
            || (!metricRule && (request.Category.HasValue || !string.IsNullOrWhiteSpace(request.ScannerKey)));
        if (!findingRule && !metricRule)
            throw new BadRequestException("A policy must target findings or one metric.");
        if (metricRule && (request.SeverityThreshold.HasValue || !string.IsNullOrWhiteSpace(request.RuleId)))
            throw new BadRequestException("A metric policy cannot also target finding severity or rule id.");
        if (metricRule && request.NewFindingsOnly)
            throw new BadRequestException("A metric policy cannot be limited to new findings.");
        if (metricRule && (!request.Operator.HasValue || !request.Threshold.HasValue))
            throw new BadRequestException("A metric policy requires an operator and threshold.");
    }

    public static void Apply(AnalysisPolicy policy, UpsertAnalysisPolicyRequest request)
    {
        var requestedKey = policy.Id != 0 && string.IsNullOrWhiteSpace(request.PolicyKey)
            ? policy.PolicyKey
            : AnalysisPolicyKey.Resolve(request.PolicyKey, request.Name);
        if (policy.Id != 0 && !string.IsNullOrWhiteSpace(policy.PolicyKey)
            && !string.Equals(policy.PolicyKey, requestedKey, StringComparison.Ordinal))
            throw new BadRequestException("A policy key cannot be changed after creation.");
        policy.PolicyKey = requestedKey;
        policy.Name = request.Name.Trim();
        policy.Category = request.Category;
        policy.ScannerKey = Normalize(request.ScannerKey);
        policy.RuleId = Normalize(request.RuleId);
        policy.SeverityThreshold = request.SeverityThreshold;
        policy.NewFindingsOnly = request.NewFindingsOnly;
        policy.MetricKey = Normalize(request.MetricKey);
        policy.Operator = request.Operator;
        policy.Threshold = request.Threshold;
        policy.BranchPattern = Normalize(request.BranchPattern);
        policy.EnvironmentPattern = Normalize(request.EnvironmentPattern);
        policy.Behavior = request.Behavior;
        policy.Priority = request.Priority;
        policy.Enabled = request.Enabled;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
