// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

internal static class PipelineRunGatePresentation
{
    public static BadgeStyle GateStyle(AnalysisGateStatus status) => status switch
    {
        AnalysisGateStatus.Passed => BadgeStyle.Success,
        AnalysisGateStatus.Warning => BadgeStyle.Warning,
        AnalysisGateStatus.Blocked or AnalysisGateStatus.Error => BadgeStyle.Danger,
        _ => BadgeStyle.Light
    };

    public static BadgeStyle SeverityStyle(AnalysisSeverity severity) =>
        AnalysisPresentation.SeverityBadge(severity);

    public static string GradeCss(AnalysisGrade? grade) => AnalysisPresentation.GradeCss(grade);

    public static string CategoryKey(AnalysisCategory category) => $"QualityGateCategory{category}";
    public static string SeverityKey(AnalysisSeverity severity) => $"AnalysisSeverity{severity}";
    public static string StatusKey(AnalysisGateStatus status) => $"AnalysisGateStatus{status}";
}
