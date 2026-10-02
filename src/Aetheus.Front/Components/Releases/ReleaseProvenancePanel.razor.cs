// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Releases;

public partial class ReleaseProvenancePanel
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public ReleaseProvenanceDto Provenance { get; set; } = default!;
}
