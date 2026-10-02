// SPDX-License-Identifier: EUPL-1.2

using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Components.Shared;

public partial class GradeBadge
{
    private static OmniTone VariantFor(AnalysisGrade grade) => grade switch
    {
        AnalysisGrade.A or AnalysisGrade.B => OmniTone.Success,
        AnalysisGrade.C => OmniTone.Warning,
        _ => OmniTone.Danger
    };

    /// <summary>The grade to draw; null renders the neutral dash.</summary>
    [Parameter] public AnalysisGrade? Value { get; set; }

    /// <summary>Tooltip, usually the localized "overall grade" wording of the calling page.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Extra CSS classes from the call site.</summary>
    [Parameter] public string? Class { get; set; }
}
