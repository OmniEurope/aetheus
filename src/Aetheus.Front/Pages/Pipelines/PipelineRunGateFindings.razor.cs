// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

using Aetheus.Front.Pages.Analysis;

public partial class PipelineRunGateFindings
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Parameter, EditorRequired] public AnalysisRunGateDto Gate { get; set; } = default!;
    [Parameter] public int? ProjectId { get; set; }

    private static string FindingHref(int findingId) => $"/analysis/findings/{findingId}";
    private void OpenFinding(int findingId) => Navigation.NavigateTo(FindingHref(findingId));
    private void OpenProjectQuality(int projectId) => Navigation.NavigateTo($"/projects/{projectId}/quality");

    private Task ExportAllAiPromptAsync()
    {
        var markdown = AnalysisAiPromptBuilder.BuildBulk(Gate.Findings, L);
        return Js.InvokeVoidAsync(
            "downloadFile",
            $"pipeline-run-{Gate.PipelineRunId}-findings-ai-prompt.md",
            markdown,
            "text/markdown").AsTask();
    }
}
