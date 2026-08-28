// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.Analysis;

public static class AnalysisGateScopes
{
    public const string Quality = "quality";
    public const string Security = "security";

    private static readonly AnalysisCategory[] QualityCategories =
    [
        AnalysisCategory.CodeQuality,
        AnalysisCategory.Coverage,
        AnalysisCategory.Duplication,
        AnalysisCategory.Architecture
    ];

    private static readonly AnalysisCategory[] SecurityCategories =
    [
        AnalysisCategory.Sast,
        AnalysisCategory.Secrets,
        AnalysisCategory.Dependencies,
        AnalysisCategory.Container,
        AnalysisCategory.InfrastructureAsCode,
        AnalysisCategory.Sbom,
        AnalysisCategory.Dast
    ];

    public static bool IsValid(string? scope) =>
        Normalize(scope) is Quality or Security;

    public static string Normalize(string? scope) =>
        scope?.Trim().ToLowerInvariant() ?? string.Empty;

    public static string ForCategory(AnalysisCategory category) =>
        QualityCategories.Contains(category) ? Quality : Security;

    public static string? ForScannerCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return null;
        return category.Trim().ToLowerInvariant() switch
        {
            "codequality" or "coverage" or "duplication" or "complexity" or "architecture" => Quality,
            _ => Security
        };
    }

    public static IReadOnlyCollection<AnalysisCategory> Categories(string scope) =>
        Normalize(scope) == Quality ? QualityCategories : SecurityCategories;
}
