// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Shared.Components.Pipelines;

/// <summary>
/// PLAN-003 2.4: the categories a <c>type: lint</c> step may publish its SARIF under
/// (<c>analysis_category</c>). Code quality stays the default; accessibility is the axe-core report of
/// the E2E suite. Both are quality-scoped, so the lint producer keeps satisfying the same gate.
/// </summary>
public static class LintAnalysisCategories
{
    /// <summary>The task variable carrying the category from the control plane to the agent.</summary>
    public const string VariableName = "AETHEUS_LINT_CATEGORY";

    public static bool TryParse(string? value, out AnalysisCategory category)
    {
        switch (value?.Trim().Replace("-", string.Empty, StringComparison.Ordinal)
                    .Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant())
        {
            case null or "" or "codequality":
                category = AnalysisCategory.CodeQuality;
                return true;
            case "accessibility":
                category = AnalysisCategory.Accessibility;
                return true;
            default:
                category = AnalysisCategory.CodeQuality;
                return false;
        }
    }
}
