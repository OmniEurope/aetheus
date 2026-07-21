// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Radzen;
using Radzen.Blazor;

namespace Aetheus.Front.Shared;

/// <summary>
/// Shared pipeline overview. Global and project scopes use the dependency graph so parents,
/// children and inverse parent links stay coherent; the server scope retains the bounded page.
/// </summary>
public partial class PipelinesList : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private PipelineRunGate RunGate { get; set; } = default!;
    [Inject] private PipelineRunDialogCoordinator RunDialogs { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private PipelineImportState ImportState { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;

    /// <summary>Project filter (project detail scope). Mutually exclusive with <see cref="ServerId"/>.</summary>
    [Parameter] public int? ProjectId { get; set; }

    /// <summary>Server filter (server detail scope). When both are null the list is global (unfiltered).</summary>
    [Parameter] public int? ServerId { get; set; }

    private bool IsServerScope => ServerId.HasValue;
    private bool IsProjectScope => ProjectId.HasValue;
    private bool IsGlobal => !IsServerScope && !IsProjectScope;

    private List<PipelineDependencyDto> _dependencyPage = [];
    private List<PipelineDependencyDto> _parents = [];
    private List<PipelineDependencyDto> _leaves = [];
    private List<PipelineRunTableItem> _recentRuns = [];
    private int _totalCount;
    private bool _loading;
    private bool _canWrite;
    private AetheusDataGrid<PipelineDependencyDto>? _grid;

    // Global scope only: search + trigger filter + import.
    private string? _search;
    private PipelineTriggerType? _triggerFilter;
    private List<object> _triggerOptions = [];
    private InputFile? _pipelineFileInput;
    private HubConnection? _hubConnection;
    private HubConnection? _entityHub;
    private readonly HashSet<int> _joinedRunGroups = [];
    // #7: the pipelines hub broadcasts every run across the org (no project/server context to filter on),
    // so coalesce a burst of (mostly unrelated) events into a single trailing refresh.
    private const int LiveRefreshDebounceMs = 1000;
    private readonly TrailingReloadCoalescer _liveCoalescer = new(LiveRefreshDebounceMs);

    protected override async Task OnInitializedAsync()
    {
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshCanWrite();

        if (IsGlobal)
        {
            _triggerOptions =
            [
                new { Text = L.Localize(PipelineTriggerType.Manual), Value = (PipelineTriggerType?)PipelineTriggerType.Manual },
                new { Text = L.Localize(PipelineTriggerType.Webhook), Value = (PipelineTriggerType?)PipelineTriggerType.Webhook },
                new { Text = L.Localize(PipelineTriggerType.Schedule), Value = (PipelineTriggerType?)PipelineTriggerType.Schedule }
            ];
        }

        if (!IsServerScope)
            await LoadOverviewAsync();

        await StartHubAsync();
    }

    private void OnPermissionsChanged()
    {
        RefreshCanWrite();
        InvokeAsync(StateHasChanged);
    }

    private void RefreshCanWrite() => _canWrite = Permissions.CanWrite(ResourceType.Pipeline);

    private async Task OnLoadData(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        _loading = true;
        try
        {
            var result = await Api.GetPipelineDependencyPageAsync(
                page, pageSize, _search, _triggerFilter, ProjectId, ServerId);
            _dependencyPage = result.Items;
            _totalCount = result.TotalCount;
        }
        finally { _loading = false; }
    }

    private IReadOnlyList<PipelineDependencyDto> FilteredParents => Filter(_parents);
    private IReadOnlyList<PipelineDependencyDto> FilteredLeaves => Filter(_leaves);

    private List<PipelineDependencyDto> Filter(IEnumerable<PipelineDependencyDto> source) => source
        .Where(item => !ProjectId.HasValue || item.ProjectId == ProjectId)
        .Where(item => string.IsNullOrWhiteSpace(_search)
            || item.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || (item.ProjectName?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false))
        .Where(item => !_triggerFilter.HasValue || item.TriggerType == _triggerFilter)
        .OrderBy(item => item.ProjectName)
        .ThenBy(item => item.Name)
        .ToList();

    private async Task LoadOverviewAsync()
    {
        _loading = true;
        try
        {
            var graphTask = Api.GetPipelineDependencyGroupsAsync();
            var recentTask = Api.GetRecentPipelineRunsAsync(ProjectId);
            await Task.WhenAll(graphTask, recentTask);
            var graph = await graphTask;
            _parents = graph.Parents;
            _leaves = graph.Leaves;
            _totalCount = Filter(_parents.Concat(_leaves)).Count;
            _recentRuns = (await recentTask)
                .Take(20)
                .Select(PipelineRunTableItem.FromRun)
                .ToList();
            await SyncRunGroupsAsync();
        }
        finally { _loading = false; }
    }

    internal async Task RunPipeline(int id, string? sourceBranch = null)
    {
        if (!await RunGate.ConfirmPreflightAsync(id, sourceBranch)) return;

        // P: if the pipeline declares queue-time parameters, collect them before launching.
        var (proceed, parameters) = await CollectRunParametersAsync(id, sourceBranch);
        if (!proceed) return;

        var outcome = await Api.TriggerPipelineRunAsync(id, parameters, sourceBranch);
        if (outcome.Value is not null)
        {
            Toast.Success("Running", "PipelineTriggered");
        }
        else if (outcome.Error is not null)
            Toast.Notify(NotificationSeverity.Error, "PipelineRunFailed", string.Join(" ", outcome.Error.Errors));
        else
            Toast.Error("PipelineRunFailed", outcome.NotFound ? "RunNotFound" : "Error");

        await ReloadGrid();
    }

    private async Task RunPipelineClick(int id, RadzenSplitButtonItem? item)
    {
        var sourceBranch = item?.Value == "branch" ? await RunDialogs.ChooseBranchAsync(id) : null;
        if (item?.Value == "branch" && sourceBranch is null) return;
        await RunPipeline(id, sourceBranch);
    }

    // P: fetch the pipeline's declared queue-time parameters; if any, open the run dialog. Returns
    // (proceed, values) - proceed is false only when the user cancels the dialog.
    private async Task<(bool Proceed, Dictionary<string, string>? Parameters)> CollectRunParametersAsync(int pipelineId, string? sourceBranch = null)
    {
        return await RunDialogs.CollectParametersAsync(pipelineId, sourceBranch);
    }

    private Task ReloadGrid() => IsServerScope
        ? _grid?.Reload() ?? Task.CompletedTask
        : LoadOverviewAsync();

    private Task ApplyFilters()
    {
        _totalCount = Filter(_parents.Concat(_leaves)).Count;
        return InvokeAsync(StateHasChanged);
    }

    private string PipelineHref(int id) => IsProjectScope
        ? $"/pipelines/{id}?projectId={ProjectId}"
        : IsServerScope ? $"/pipelines/{id}?serverId={ServerId}" : $"/pipelines/{id}";

    private void NavigateToPipeline(int id) => Nav.NavigateTo(PipelineHref(id));

    private void EditPipeline(int id)
    {
        var href = PipelineHref(id);
        Nav.NavigateTo($"{href}{(href.Contains('?') ? '&' : '?')}tab=edit");
    }

    private Task OnPipelineHubEvent(int pipelineId) =>
        (IsGlobal || IsProjectScope || _dependencyPage.Any(item => item.Id == pipelineId))
            ? InvokeAsync(() => _liveCoalescer.RequestAsync(() => InvokeAsync(RefreshLiveAsync)))
            : Task.CompletedTask;

    private async Task OnPipelineRunEvent(int runId)
    {
        try
        {
            var run = await Api.GetPipelineRunAsync(runId);
            if (run is not null) await OnPipelineHubEvent(run.PipelineId);
        }
        catch (HttpRequestException) { }
    }

    // Hub-driven refresh. Global scope is server-paginated, so re-paging re-fetches; the client
    // scopes hold the full list in memory, so they must re-fetch it first to pick up new run state.
    private async Task RefreshLiveAsync()
    {
        await ReloadGrid();
    }

    // --- Global scope: filters + create/import (toolbar) ---

    private async Task ClearFilters()
    {
        _search = null;
        _triggerFilter = null;
        await ApplyFilters();
    }

    private void NewPipeline() => Nav.NavigateTo("/pipelines/new");

    // S-FEAT-17: import a pipeline from an uploaded YAML file - read + validate the name, then stash
    // it for the new-pipeline form (which collects the required owner before saving).
    private async Task OnImportPipelineClick()
    {
        if (_pipelineFileInput?.Element is not null)
            await Js.InvokeVoidAsync("HTMLElement.prototype.click.call", _pipelineFileInput.Element);
    }

    private async Task OnPipelineFileSelected(InputFileChangeEventArgs e)
    {
        var file = e.File;
        if (file.Size == 0 || file.Size > 50_000) return;

        using var reader = new StreamReader(file.OpenReadStream(50_000));
        var yaml = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(yaml)) return;

        string? name = null;
        try
        {
            var def = await Api.ValidatePipelineYamlAsync(yaml);
            name = def?.Name;
        }
        catch (HttpRequestException) { /* fall back to a blank name */ }

        ImportState.Set(yaml, name);
        Nav.NavigateTo("/pipelines/new");
    }

    // --- Global scope: live refresh via the pipelines + entities hubs ---

    private async Task StartHubAsync()
    {
        try
        {
            _hubConnection = HubFactory.Create("pipelines");
            _hubConnection.On<int, int>("PipelineRunStarted", (_, pipelineId) => OnPipelineHubEvent(pipelineId));
            _hubConnection.On<int, PipelineStatus>("PipelineRunCompleted", (runId, _) => OnPipelineRunEvent(runId));
            _hubConnection.On<int>("PipelineRunCancelled", runId => OnPipelineRunEvent(runId));
            _hubConnection.On<int?>("StepStarted", _ => OnPipelineProgressEvent());
            _hubConnection.On<int?, TaskExecutionStatus>("StepCompleted", (_, _) => OnPipelineProgressEvent());
            _hubConnection.On<int, string, string>("ApprovalRequired", (_, _, _) => OnPipelineProgressEvent());
            _hubConnection.On<int, string, string>("ApprovalResolved", (_, _, _) => OnPipelineProgressEvent());
            // Group membership is per-connection and lost on auto-reconnect - re-join + reload.
            _hubConnection.RejoinOnReconnect(async () =>
            {
                await _hubConnection.InvokeAsync("JoinPipelineUpdatesGroup");
                _joinedRunGroups.Clear();
                await SyncRunGroupsAsync();
                await ReloadGrid();
            });
            await _hubConnection.StartAsync();
            await _hubConnection.InvokeAsync("JoinPipelineUpdatesGroup");
            await SyncRunGroupsAsync();
        }
        catch { /* best-effort */ }

        try
        {
            _entityHub = HubFactory.Create("entities");
            _entityHub.On<ResourceType, int, string>("EntityChanged", (type, id, _) =>
                type == ResourceType.Pipeline ? OnPipelineHubEvent(id) : Task.CompletedTask);
            _entityHub.RejoinOnReconnect(async () =>
            {
                await _entityHub.InvokeAsync("JoinEntityUpdates", ResourceType.Pipeline);
                await ReloadGrid();
            });
            await _entityHub.StartAsync();
            await _entityHub.InvokeAsync("JoinEntityUpdates", ResourceType.Pipeline);
        }
        catch { /* best-effort */ }
    }

    private Task OnPipelineProgressEvent() => !IsServerScope
        ? InvokeAsync(() => _liveCoalescer.RequestAsync(() => InvokeAsync(RefreshLiveAsync)))
        : Task.CompletedTask;

    private async Task SyncRunGroupsAsync()
    {
        if (IsServerScope || _hubConnection?.State != HubConnectionState.Connected) return;

        foreach (var run in _recentRuns.Where(run => run.Status is PipelineStatus.Running
                     or PipelineStatus.Pending or PipelineStatus.WaitingForApproval))
        {
            if (!_joinedRunGroups.Add(run.RunId)) continue;
            try { await _hubConnection.InvokeAsync("JoinPipelineRunGroup", run.RunId); }
            catch { _joinedRunGroups.Remove(run.RunId); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        if (_hubConnection is not null)
        {
            try { await _hubConnection.InvokeAsync("LeavePipelineUpdatesGroup"); } catch { /* best-effort */ }
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
        if (_entityHub is not null)
        {
            try { await _entityHub.InvokeAsync("LeaveEntityUpdates", ResourceType.Pipeline); } catch { /* best-effort */ }
            await _entityHub.DisposeAsync();
            _entityHub = null;
        }
    }
}
