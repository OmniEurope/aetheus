// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

public static class AnalysisPresentation
{
    public static OmniTone SeverityBadge(AnalysisSeverity severity) => severity switch
    {
        AnalysisSeverity.Critical => OmniTone.Danger,
        AnalysisSeverity.High => OmniTone.Warning,
        AnalysisSeverity.Medium => OmniTone.Accent,
        AnalysisSeverity.Low => OmniTone.Neutral,
        _ => OmniTone.Neutral
    };

    /// <summary>The badge colour of a finding's status, the same in the findings list and in a finding's
    /// decision history.</summary>
    public static OmniTone FindingStatusBadge(AnalysisFindingStatus status) => status switch
    {
        AnalysisFindingStatus.Accepted => OmniTone.Warning,
        AnalysisFindingStatus.FalsePositive => OmniTone.Accent,
        AnalysisFindingStatus.Mitigated => OmniTone.Success,
        _ => OmniTone.Neutral
    };
}
