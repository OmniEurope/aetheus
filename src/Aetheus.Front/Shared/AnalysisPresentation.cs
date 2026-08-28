// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Shared;

public static class AnalysisPresentation
{
    public static BadgeStyle SeverityBadge(AnalysisSeverity severity) => severity switch
    {
        AnalysisSeverity.Critical => BadgeStyle.Danger,
        AnalysisSeverity.High => BadgeStyle.Warning,
        AnalysisSeverity.Medium => BadgeStyle.Info,
        AnalysisSeverity.Low => BadgeStyle.Light,
        _ => BadgeStyle.Light
    };

    public static string GradeCss(AnalysisGrade? grade) =>
        $"analysis-grade-{grade?.ToString().ToLowerInvariant() ?? "na"}";
}
