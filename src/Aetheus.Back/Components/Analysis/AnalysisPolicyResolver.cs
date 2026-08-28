// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Analysis;

internal static class AnalysisPolicyResolver
{
    public static List<ResolvedAnalysisPolicy> Resolve(
        int? organizationId,
        int? projectId,
        IEnumerable<AnalysisPolicy> persistedPolicies)
    {
        var targetScope = projectId.HasValue
            ? AnalysisPolicyScope.Project
            : organizationId.HasValue
                ? AnalysisPolicyScope.Organization
                : AnalysisPolicyScope.Global;
        var candidates = AnalysisDefaultPolicyCatalog.Policies
            .Select(policy => new Candidate(policy, AnalysisPolicyScope.System))
            .Concat(persistedPolicies
                .Select(policy => ToCandidate(policy, organizationId, projectId))
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!));

        return candidates
            .GroupBy(candidate => AnalysisPolicyKey.ForExisting(candidate.Policy), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = group
                    .OrderByDescending(candidate => candidate.Scope)
                    .ThenByDescending(candidate => candidate.Policy.Version)
                    .ThenByDescending(candidate => candidate.Policy.Id)
                    .ToList();
                var selected = ordered[0];
                var overridden = ordered.Skip(1).FirstOrDefault();
                return new ResolvedAnalysisPolicy(
                    selected.Policy,
                    selected.Scope,
                    selected.Scope < targetScope,
                    overridden is not null,
                    overridden?.Scope);
            })
            .OrderByDescending(item => item.Policy.Priority)
            .ThenBy(item => AnalysisPolicyKey.ForExisting(item.Policy), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Candidate? ToCandidate(AnalysisPolicy policy, int? organizationId, int? projectId)
    {
        if (policy.ProjectId.HasValue)
            return policy.ProjectId == projectId
                ? new Candidate(policy, AnalysisPolicyScope.Project)
                : null;
        if (policy.OrganizationId.HasValue)
            return policy.OrganizationId == organizationId
                ? new Candidate(policy, AnalysisPolicyScope.Organization)
                : null;
        return new Candidate(policy, AnalysisPolicyScope.Global);
    }

    private sealed record Candidate(AnalysisPolicy Policy, AnalysisPolicyScope Scope);
}
