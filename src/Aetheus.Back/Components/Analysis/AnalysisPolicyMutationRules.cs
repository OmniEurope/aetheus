// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Analysis;

internal static class AnalysisPolicyMutationRules
{
    public static void ValidateBatch(
        IReadOnlyCollection<AnalysisPolicy> existing,
        IReadOnlyList<AnalysisPolicyBatchItemRequest> items)
    {
        var duplicatePolicyId = items
            .Where(item => item.PolicyId.HasValue)
            .GroupBy(item => item.PolicyId!.Value)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePolicyId is not null)
            throw new BadRequestException(
                $"Analysis policy {duplicatePolicyId.Key} appears more than once in the batch.");
        foreach (var item in items)
            AnalysisPolicyRequestValidator.Validate(item.Policy);

        var prospective = existing.Select(Clone).ToList();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var current = item.PolicyId.HasValue
                ? prospective.FirstOrDefault(policy => policy.Id == item.PolicyId.Value)
                    ?? throw new NotFoundException("Analysis policy not found in the selected scope.")
                : null;
            EnsureUnique(prospective, item.Policy, current?.Id, current?.PolicyKey);
            var candidate = current is null ? new AnalysisPolicy() : Clone(current);
            AnalysisPolicyRequestValidator.Apply(candidate, item.Policy);
            if (current is not null)
                prospective.Remove(current);
            if (candidate.Id == 0)
                candidate.Id = -(index + 1);
            prospective.Add(candidate);
        }
    }

    public static void ApplyPreviewCandidate(
        List<AnalysisPolicy> policies,
        int? organizationId,
        int? projectId,
        PreviewAnalysisPolicySetRequest request)
    {
        if (request.Candidate is null)
            return;

        AnalysisPolicyRequestValidator.Validate(request.Candidate);
        var localPolicies = policies.Where(item =>
            item.OrganizationId == organizationId && item.ProjectId == projectId).ToList();
        var current = request.PolicyId.HasValue
            ? localPolicies.FirstOrDefault(item => item.Id == request.PolicyId.Value)
                ?? throw new NotFoundException("Analysis policy not found in the selected scope.")
            : null;
        EnsureUnique(localPolicies, request.Candidate, current?.Id, current?.PolicyKey);
        var candidate = current is null
            ? new AnalysisPolicy
            {
                OrganizationId = organizationId,
                ProjectId = projectId,
                Version = 1
            }
            : Clone(current);
        AnalysisPolicyRequestValidator.Apply(candidate, request.Candidate);
        if (current is not null)
        {
            candidate.Version++;
            policies.Remove(current);
        }
        policies.Add(candidate);
    }

    public static void EnsureUnique(
        IEnumerable<AnalysisPolicy> existing,
        UpsertAnalysisPolicyRequest request,
        int? policyId,
        string? currentKey)
    {
        var key = string.IsNullOrWhiteSpace(request.PolicyKey) && !string.IsNullOrWhiteSpace(currentKey)
            ? currentKey
            : AnalysisPolicyKey.Resolve(request.PolicyKey, request.Name);
        if (existing.Any(item => item.Id != policyId
            && string.Equals(AnalysisPolicyKey.ForExisting(item), key, StringComparison.OrdinalIgnoreCase)))
            throw new ConflictException("An analysis policy with this key already exists in the selected scope.");
        if (existing.Any(item => item.Id != policyId
            && string.Equals(item.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new ConflictException("An analysis policy with this name already exists in the selected scope.");
    }

    private static AnalysisPolicy Clone(AnalysisPolicy policy) => new()
    {
        Id = policy.Id,
        OrganizationId = policy.OrganizationId,
        ProjectId = policy.ProjectId,
        PolicyKey = policy.PolicyKey,
        Name = policy.Name,
        Category = policy.Category,
        ScannerKey = policy.ScannerKey,
        RuleId = policy.RuleId,
        SeverityThreshold = policy.SeverityThreshold,
        NewFindingsOnly = policy.NewFindingsOnly,
        MetricKey = policy.MetricKey,
        Operator = policy.Operator,
        Threshold = policy.Threshold,
        BranchPattern = policy.BranchPattern,
        EnvironmentPattern = policy.EnvironmentPattern,
        Behavior = policy.Behavior,
        Priority = policy.Priority,
        Enabled = policy.Enabled,
        Version = policy.Version,
        CreatedAt = policy.CreatedAt,
        UpdatedAt = policy.UpdatedAt
    };
}
