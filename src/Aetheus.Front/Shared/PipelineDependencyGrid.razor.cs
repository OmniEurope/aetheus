// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;
using Radzen.Blazor;

namespace Aetheus.Front.Shared;

public partial class PipelineDependencyGrid
{
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public IReadOnlyList<PipelineDependencyDto> Items { get; set; } = [];
    [Parameter, EditorRequired] public Func<int, string> PipelineHref { get; set; } = default!;
    [Parameter, EditorRequired] public Func<int, RadzenSplitButtonItem?, Task> RunPipeline { get; set; } = default!;
    [Parameter] public bool ShowsChildren { get; set; }
    [Parameter] public bool GroupByProject { get; set; }
    [Parameter] public bool FillHeight { get; set; }
    [Parameter] public bool Virtualize { get; set; }
    [Parameter] public int PageSize { get; set; } = 10;
    [Parameter] public bool CanWrite { get; set; }

    private IReadOnlyList<PipelineDependencyDto> DisplayItems => Items;
    private string GridCssClass => $"pipeline-dependency-grid aetheus-clickable-rows{(FillHeight ? " pipeline-grid-fill" : string.Empty)}{(Virtualize ? " pipeline-grid-virtualized pipeline-grid-five-rows" : string.Empty)}";
    private string RelationListCss => $"pipeline-relation-list {(ShowsChildren ? "pipeline-relation-list-execution" : "pipeline-relation-list-parents")}";

    private IReadOnlyList<PipelineDependencyReferenceDto> Relations(PipelineDependencyDto pipeline) =>
        ShowsChildren
            ? pipeline.References
            : pipeline.Parents.OrderBy(parent => parent.Name, StringComparer.OrdinalIgnoreCase).ToList();

    private void OnRowRender(RowRenderEventArgs<PipelineDependencyDto> args) =>
        args.Expandable = args.Data is not null && Relations(args.Data).Count > 0;

    private void OnRowClick(DataGridRowMouseEventArgs<PipelineDependencyDto> args)
    {
        if (args.Data is not null)
            Nav.NavigateTo(PipelineHref(args.Data.Id));
    }

    private void EditPipeline(int id)
    {
        var href = PipelineHref(id);
        Nav.NavigateTo($"{href}{(href.Contains('?') ? '&' : '?')}tab=edit");
    }
}
