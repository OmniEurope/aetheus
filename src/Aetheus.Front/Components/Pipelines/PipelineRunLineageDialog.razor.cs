// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

public partial class PipelineRunLineageDialog
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    /// <summary>The follow-up runs, oldest first, as the lineage endpoint returns them.</summary>
    [Parameter] public IReadOnlyList<PipelineRunLinkDto> Runs { get; set; } = [];
    [Parameter] public int? ProjectId { get; set; }
    [Parameter] public int? ServerId { get; set; }
}
