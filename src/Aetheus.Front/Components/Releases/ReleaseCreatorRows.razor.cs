// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Releases;

public partial class ReleaseCreatorRows
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public ReleaseDto Release { get; set; } = default!;

    /// <summary>Null while loading or when the provenance could not be read.</summary>
    [Parameter] public ReleaseProvenanceDto? Provenance { get; set; }

    /// <summary>The last run that recorded the release, when the creator is unknown. Without the
    /// provenance the release row's own run is that same fact.</summary>
    private int? LastRecordedRunId => Provenance is null
        ? Release.PipelineRunId
        : Provenance.LastRecordedBy?.RunId;
}
