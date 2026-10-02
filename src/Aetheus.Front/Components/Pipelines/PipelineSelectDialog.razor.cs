// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

public partial class PipelineSelectDialog
{
    [Parameter] public List<PipelineDto> Pipelines { get; set; } = [];

    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private int? _selectedId;
}
