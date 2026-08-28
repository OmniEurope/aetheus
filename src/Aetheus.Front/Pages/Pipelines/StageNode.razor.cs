// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components.Web;

namespace Aetheus.Front.Pages.Pipelines;

public partial class StageNode
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public PipelineStageDefinition Stage { get; set; } = default!;
    [Parameter] public string ElementId { get; set; } = string.Empty;
    [Parameter] public double X { get; set; }
    [Parameter] public double Y { get; set; }
    [Parameter] public bool IsSelected { get; set; }
    [Parameter] public bool ReadOnly { get; set; }
    [Parameter] public string? Provenance { get; set; }
    // Supplied localized by the consumer (VisualPipelineEditor passes L["AddStep"]); no hardcoded default.
    [Parameter] public string AddStepText { get; set; } = string.Empty;
    [Parameter] public List<string> Warnings { get; set; } = [];
    [Parameter] public EventCallback OnSelect { get; set; }
    [Parameter] public EventCallback OnDelete { get; set; }
    [Parameter] public EventCallback OnDuplicate { get; set; }
    [Parameter] public EventCallback OnAddStep { get; set; }
    [Parameter] public EventCallback<int> OnEditStep { get; set; }

    private async Task OnSelectClicked() => await OnSelect.InvokeAsync();
    private async Task OnNodeKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or " " or "Spacebar")
            await OnSelect.InvokeAsync();
    }
    private async Task OnDeleteClicked() => await OnDelete.InvokeAsync();
    private async Task OnDuplicateClicked() => await OnDuplicate.InvokeAsync();
    private async Task OnAddStepClicked() => await OnAddStep.InvokeAsync();
    private async Task OnEditStepClicked(int index) => await OnEditStep.InvokeAsync(index);
}
