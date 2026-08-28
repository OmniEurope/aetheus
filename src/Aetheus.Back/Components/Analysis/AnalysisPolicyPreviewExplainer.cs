// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Analysis;

internal static class AnalysisPolicyPreviewExplainer
{
    public static List<string> ExpectedProducers(IEnumerable<ResolvedAnalysisPolicy> policies) =>
        policies.Where(item => item.IsEffective)
            .Select(item => Producer(item.Policy))
            .Where(item => item is not null)
            .Select(item => item!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static List<string> Conflicts(IEnumerable<AnalysisPolicy> policies) =>
        policies.GroupBy(
                item => (
                    item.OrganizationId,
                    item.ProjectId,
                    Key: AnalysisPolicyKey.ForExisting(item).ToLowerInvariant()))
            .Where(group => group.Count() > 1)
            .Select(group =>
                $"{group.Key.Key}: {group.Count()} rules in the same scope")
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string? Producer(AnalysisPolicy policy)
    {
        if (!string.IsNullOrWhiteSpace(policy.ScannerKey))
            return $"scanner:{policy.ScannerKey}";
        if (!string.IsNullOrWhiteSpace(policy.MetricKey))
        {
            if (policy.MetricKey.StartsWith("coverage.", StringComparison.OrdinalIgnoreCase))
                return "coverage";
            if (policy.MetricKey.StartsWith("duplication.", StringComparison.OrdinalIgnoreCase))
                return "scanner:jscpd";
            if (policy.MetricKey.StartsWith("complexity.", StringComparison.OrdinalIgnoreCase)
                || policy.MetricKey.StartsWith("architecture.", StringComparison.OrdinalIgnoreCase))
                return "complexity";
            return $"metric:{policy.MetricKey}";
        }
        return policy.Category switch
        {
            AnalysisCategory.Coverage => "coverage",
            AnalysisCategory.CodeQuality or AnalysisCategory.Duplication => "scanner:code-quality",
            AnalysisCategory.Architecture => "complexity",
            { } category => $"scanner:{category.ToString().ToLowerInvariant()}",
            _ => null
        };
    }
}
