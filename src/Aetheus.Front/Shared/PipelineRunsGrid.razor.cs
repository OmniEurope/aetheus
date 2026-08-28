// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Shared;

public partial class PipelineRunsGrid
{
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialogs { get; set; } = default!;

    [Parameter, EditorRequired] public IReadOnlyList<PipelineRunDto> Items { get; set; } = [];
    [Parameter] public bool GroupByProject { get; set; }
    [Parameter] public bool FillHeight { get; set; }
    [Parameter] public bool Virtualize { get; set; }
    [Parameter] public bool AllowPaging { get; set; } = true;
    [Parameter] public bool Compact { get; set; }
    [Parameter] public bool ShowDurationInCompact { get; set; }
    [Parameter] public bool IsLoading { get; set; }
    [Parameter] public int PageSize { get; set; } = 10;
    [Parameter] public int MaxGroups { get; set; } = int.MaxValue;
    [Parameter] public int? ServerId { get; set; }
    [Parameter] public string? CssClass { get; set; }
    [Parameter] public string EmptyTitleKey { get; set; } = "NoRunningPipelines";
    [Parameter] public string EmptyDescriptionKey { get; set; } = "NoRunningPipelinesHint";

    /// <summary>Hidden when the table already lives inside a single pipeline, where repeating its name
    /// on every row carries nothing.</summary>
    [Parameter] public bool ShowPipelineColumn { get; set; } = true;

    /// <summary>Raised after a run was cancelled, so the host can refresh its own data.</summary>
    [Parameter] public EventCallback OnRunCancelled { get; set; }

    /// <summary>Git's own abbreviation length for a readable, still unambiguous commit reference.</summary>
    private const int CommitDisplayLength = 8;

    private RadzenDataGrid<PipelineRunTableItem>? _grid;
    private int? _cancelling;
    private IReadOnlyList<PipelineRunDto>? _groupedSource;
    private IReadOnlyList<PipelineRunTableItem> _displayItems = [];
    private int? _expandedRunId;
    private bool _restoreExpandedRow;

    private IReadOnlyList<PipelineRunTableItem> DisplayItems => _displayItems;
    private bool UsesVirtualization => Virtualize && !DisplayItems.Any(run => run.LinkedRuns.Count > 0);
    private string GridCssClass =>
        $"pipeline-runs-grid aetheus-clickable-rows{(DisplayItems.Any(run => run.LinkedRuns.Count > 0) ? " pipeline-runs-grouped" : string.Empty)}{(Compact ? " pipeline-runs-compact" : string.Empty)}{(FillHeight ? " pipeline-grid-fill" : string.Empty)}{(UsesVirtualization ? " pipeline-grid-virtualized" : string.Empty)}{(string.IsNullOrWhiteSpace(CssClass) ? string.Empty : $" {CssClass}")}";

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(Items, _groupedSource))
            return;

        _groupedSource = Items;
        _displayItems = PipelineRunTableItem.GroupRuns(Items)
            .Take(Math.Max(0, MaxGroups))
            .ToList();

        if (_expandedRunId.HasValue)
        {
            var expandedRun = _displayItems.FirstOrDefault(run => run.RunId == _expandedRunId.Value);
            if (expandedRun is null || expandedRun.LinkedRuns.Count == 0)
                _expandedRunId = null;
            else
                _restoreExpandedRow = true;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_restoreExpandedRow || _grid is null || !_expandedRunId.HasValue)
            return;

        _restoreExpandedRow = false;
        var expandedRun = _displayItems.FirstOrDefault(run => run.RunId == _expandedRunId.Value);
        if (expandedRun is not null)
            await _grid.ExpandRow(expandedRun);
    }

    private string PipelineHref(PipelineRunTableItem run) => PipelineRunTablePresentation.PipelineHref(run, ServerId);

    private string RunHref(PipelineRunTableItem run) => PipelineRunTablePresentation.RunHref(run, ServerId);

    private void OnRowClick(DataGridRowMouseEventArgs<PipelineRunTableItem> args)
    {
        if (args.Data is not null)
            Nav.NavigateTo(RunHref(args.Data));
    }

    private static void OnRowRender(RowRenderEventArgs<PipelineRunTableItem> args) =>
        args.Expandable = args.Data is not null && args.Data.LinkedRuns.Count > 0;

    /// <summary>Only a run that is still going anywhere can be cancelled. A run whose cancellation was
    /// already accepted keeps running its mandatory teardown, so offering the button again would just
    /// repeat a request the backend has already durably taken.</summary>
    private static bool CanCancel(PipelineRunTableItem run) =>
        !run.CancellationRequested
        && run.Status is PipelineStatus.Pending or PipelineStatus.Running or PipelineStatus.WaitingForApproval;

    private async Task CancelRunAsync(PipelineRunTableItem run)
    {
        if (_cancelling.HasValue || !CanCancel(run))
            return;

        var confirmed = await Dialogs.Confirm(
            L["CancelRunConfirm"].Value,
            L["CancelRun"].Value,
            new ConfirmOptions
            {
                OkButtonText = L["CancelRun"].Value,
                CancelButtonText = L["Back"].Value
            });
        if (confirmed != true)
            return;

        _cancelling = run.RunId;
        try
        {
            var status = await Api.Pipelines.CancelPipelineRunAsync(run.RunId);
            if (status.Success)
            {
                Toast.Info("RunCancelled", "RunCancelledDetail");
                await OnRunCancelled.InvokeAsync();
            }
            else if (status.Forbidden)
            {
                Toast.Error("PipelineRunFailed", "PipelineRunForbidden");
            }
            else
            {
                Toast.Error("PipelineRunFailed", "Error");
            }
        }
        finally
        {
            _cancelling = null;
        }
    }

    private static string? ShortCommit(PipelineRunTableItem run) =>
        string.IsNullOrWhiteSpace(run.CommitHash)
            ? null
            : run.CommitHash.Length <= CommitDisplayLength ? run.CommitHash : run.CommitHash[..CommitDisplayLength];

    private static string? BranchHref(PipelineRunTableItem run) =>
        PipelineRunTablePresentation.BranchHref(run);

    private static string? CommitHref(PipelineRunTableItem run) =>
        PipelineRunTablePresentation.CommitHref(run);

    private void OnRowExpand(PipelineRunTableItem run) => _expandedRunId = run.RunId;

    private void OnRowCollapse(PipelineRunTableItem run)
    {
        if (!_restoreExpandedRow && _expandedRunId == run.RunId)
            _expandedRunId = null;
    }
}
