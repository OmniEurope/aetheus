// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;
using Radzen.Blazor;

namespace Aetheus.Front.Shared;

public partial class PipelineRunsGrid
{
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public IReadOnlyList<PipelineRunTableItem> Items { get; set; } = [];
    [Parameter] public bool GroupByProject { get; set; }
    [Parameter] public bool FillHeight { get; set; }
    [Parameter] public bool Virtualize { get; set; }
    [Parameter] public int PageSize { get; set; } = 10;
    [Parameter] public string EmptyTitleKey { get; set; } = "NoRunningPipelines";
    [Parameter] public string EmptyDescriptionKey { get; set; } = "NoRunningPipelinesHint";

    private IReadOnlyList<PipelineRunTableItem> DisplayItems => Items;
    private string GridCssClass => $"pipeline-runs-grid aetheus-clickable-rows{(FillHeight ? " pipeline-grid-fill" : string.Empty)}{(Virtualize ? " pipeline-grid-virtualized" : string.Empty)}";

    private static string PipelineHref(PipelineRunTableItem run) => run.ProjectId.HasValue
        ? $"/pipelines/{run.PipelineId}?projectId={run.ProjectId}"
        : $"/pipelines/{run.PipelineId}";

    private static string RunHref(PipelineRunTableItem run) => run.ProjectId.HasValue
        ? $"/pipelines/runs/{run.RunId}?projectId={run.ProjectId}"
        : $"/pipelines/runs/{run.RunId}";

    private void OnRowClick(DataGridRowMouseEventArgs<PipelineRunTableItem> args)
    {
        if (args.Data is not null)
            Nav.NavigateTo(RunHref(args.Data));
    }

    private static bool IsWindows(string? os) => os?.Contains("Windows", StringComparison.OrdinalIgnoreCase) == true;
}
