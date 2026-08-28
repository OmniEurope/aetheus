// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Pipelines;
using Microsoft.AspNetCore.Components.Forms;

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
    [Inject] private ILogger<PipelinesList> Logger { get; set; } = default!;

    /// <summary>Project filter (project detail scope). Mutually exclusive with <see cref="ServerId"/>.</summary>
    [Parameter] public int? ProjectId { get; set; }

    /// <summary>Server filter (server detail scope). When both are null the list is global (unfiltered).</summary>
    [Parameter] public int? ServerId { get; set; }

    /// <summary>Optional model filter supplied by the unified global pipelines route.</summary>
    [Parameter] public int? TemplateId { get; set; }

    private bool IsServerScope => ServerId.HasValue;
    private bool IsProjectScope => ProjectId.HasValue;
    private bool IsGlobal => !IsServerScope && !IsProjectScope;

    private List<PipelineDependencyDto> _parents = [];
    private List<PipelineDependencyDto> _leaves = [];
    private List<PipelineRunDto> _recentRuns = [];
    private Dictionary<int, PipelineFleetItemDto> _fleetItems = [];
    private List<PipelineTemplateSummaryDto> _templateOptions = [];
    private HashSet<int> _favoritePipelineIds = [];
    private readonly HashSet<int> _pendingFavoritePipelineIds = [];
    private int _totalCount;
    private bool _loading;
    private bool _canWrite;
    private bool _favoritesOnly;
    private int _catalogTabIndex;
    private bool _catalogInitialized;

    // Global scope only: search + trigger filter + import.
    private string? _search;
    private PipelineTriggerType? _triggerFilter;
    private int? _templateFilterId;
    private bool _fleetAvailable;
    private List<object> _triggerOptions = [];
    private InputFile? _pipelineFileInput;
    private HubConnection? _hubConnection;
    private HubConnection? _entityHub;
    private readonly HashSet<int> _joinedRunGroups = [];
    // #7: the pipelines hub broadcasts every run across the org (no project/server context to filter on),
    // so coalesce a burst of (mostly unrelated) events into a single trailing refresh.
    private const int LiveRefreshDebounceMs = 1000;
    private readonly TrailingReloadCoalescer _liveCoalescer = new(LiveRefreshDebounceMs);
    private readonly CancellationTokenSource _liveLifetime = new();
    private Task? _liveRecoveryTask;

    protected override void OnParametersSet()
    {
        if (_templateFilterId == TemplateId) return;
        _templateFilterId = TemplateId;
        if (_catalogInitialized)
            SelectAvailableCatalogTab();
    }

    protected override async Task OnInitializedAsync()
    {
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshCanWrite();

        _triggerOptions =
        [
            new { Text = L.Localize(PipelineTriggerType.Manual), Value = (PipelineTriggerType?)PipelineTriggerType.Manual },
            new { Text = L.Localize(PipelineTriggerType.Webhook), Value = (PipelineTriggerType?)PipelineTriggerType.Webhook },
            new { Text = L.Localize(PipelineTriggerType.Schedule), Value = (PipelineTriggerType?)PipelineTriggerType.Schedule }
        ];

        await LoadOverviewAsync();

        await StartHubAsync();
    }

    private void OnPermissionsChanged()
    {
        RefreshCanWrite();
        InvokeAsync(StateHasChanged);
    }

    private void RefreshCanWrite() => _canWrite = Permissions.CanWrite(ResourceType.Pipeline);

    private IReadOnlyList<PipelineDependencyDto> FilteredParents => Filter(_parents);
    private IReadOnlyList<PipelineDependencyDto> FilteredLeaves => Filter(_leaves);
    private int FilteredCatalogCount => FilteredParents.Count + FilteredLeaves.Count;
    private IReadOnlyList<PipelineDependencyDto> FavoritePipelines => ScopeFilter(_parents.Concat(_leaves))
        .Where(item => _favoritePipelineIds.Contains(item.Id))
        .OrderBy(item => item.ProjectName)
        .ThenBy(item => item.Name)
        .ToList();

    private IEnumerable<PipelineDependencyDto> ScopeFilter(IEnumerable<PipelineDependencyDto> source) => source
        .Where(item => !ProjectId.HasValue || item.ProjectId == ProjectId);

    private List<PipelineDependencyDto> Filter(IEnumerable<PipelineDependencyDto> source) => ScopeFilter(source)
        .Where(item => !_favoritesOnly || _favoritePipelineIds.Contains(item.Id))
        .Where(MatchesTemplateFilter)
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
            var graphTask = Api.Pipelines.GetPipelineDependencyGroupsAsync(ServerId);
            var recentTask = Api.Pipelines.GetRecentPipelineRunsAsync(ProjectId, ServerId);
            var favoritesTask = Api.Pipelines.GetPipelineFavoritesAsync();
            var fleetTask = LoadFleetItemsAsync();
            await Task.WhenAll(graphTask, recentTask, favoritesTask, fleetTask);
            var graph = await graphTask;
            _parents = graph.Parents;
            _leaves = graph.Leaves;
            _favoritePipelineIds = (await favoritesTask).PipelineIds.ToHashSet();
            _totalCount = ScopeFilter(_parents.Concat(_leaves)).Count();
            if (!_catalogInitialized || _templateFilterId.HasValue)
            {
                SelectAvailableCatalogTab();
                _catalogInitialized = true;
            }
            _recentRuns = (await recentTask)
                .Take(20)
                .ToList();
            await SyncRunGroupsAsync();
        }
        finally { _loading = false; }
    }

    private async Task LoadFleetItemsAsync()
    {
        try
        {
            var items = new List<PipelineFleetItemDto>();
            var page = 1;
            while (true)
            {
                var result = await Api.Pipelines.GetPipelineFleetAsync(page, PaginationRequest.MaxPageSize);
                items.AddRange(result.Items);
                if (items.Count >= result.TotalCount || result.Items.Count == 0)
                    break;
                page++;
            }

            _fleetItems = items
                .DistinctBy(item => item.PipelineId)
                .ToDictionary(item => item.PipelineId);
            _templateOptions = items
                .Where(item => item.TemplateId.HasValue && !string.IsNullOrWhiteSpace(item.TemplateName))
                .DistinctBy(item => item.TemplateId)
                .OrderBy(item => item.TemplateName, StringComparer.OrdinalIgnoreCase)
                .Select(item => new PipelineTemplateSummaryDto
                {
                    Id = item.TemplateId!.Value,
                    Name = item.TemplateName!
                })
                .ToList();
            if (items.Any(item => item.TemplateId is null && string.IsNullOrWhiteSpace(item.TemplateName)))
                _templateOptions.Insert(0, new PipelineTemplateSummaryDto
                {
                    Id = -1,
                    Name = L["AutonomousPipeline"]
                });
            _fleetAvailable = true;
        }
        catch (HttpRequestException)
        {
            _fleetItems = [];
            _templateOptions = [];
            _fleetAvailable = false;
        }
    }

    internal async Task RunPipeline(int id, string? sourceBranch = null)
    {
        if (!await RunGate.ConfirmPreflightAsync(id, sourceBranch)) return;

        // P: take the declared parameters at their defaults; a dialog opens only when a required one
        // has no default and the run cannot be built without an answer.
        var (proceed, parameters) = await RunDialogs.ResolveDefaultParametersAsync(id, sourceBranch);
        if (!proceed) return;

        await TriggerAsync(id, sourceBranch, parameters);
    }

    private async Task RunPipeline(int id, PipelineLaunchChoice choice)
    {
        if (!await RunGate.ConfirmPreflightAsync(id, choice.SourceBranch)) return;
        await TriggerAsync(id, choice.SourceBranch, choice.Parameters);
    }

    private async Task TriggerAsync(int id, string? sourceBranch, Dictionary<string, string>? parameters)
    {
        var outcome = await Api.Pipelines.TriggerPipelineRunAsync(id, parameters, sourceBranch);
        if (outcome.Value is not null)
        {
            Toast.Success("Running", "PipelineTriggered");
            if (!IsServerScope)
            {
                _recentRuns = _recentRuns
                    .Prepend(outcome.Value)
                    .DistinctBy(run => run.Id)
                    .OrderByDescending(run => run.StartedAt)
                    .Take(20)
                    .ToList();
                await InvokeAsync(StateHasChanged);
            }
        }
        else if (outcome.Error is not null)
            Toast.Notify(NotificationSeverity.Error, "PipelineRunFailed", string.Join(" ", outcome.Error.Errors));
        else
            Toast.Error("PipelineRunFailed", outcome.NotFound ? "RunNotFound" : "Error");

        await ReloadGrid();
    }

    private async Task RunPipelineClick(int id, RadzenSplitButtonItem? item)
    {
        if (item?.Value != "options")
        {
            await RunPipeline(id);
            return;
        }

        var choice = await RunDialogs.ConfigureLaunchAsync(id);
        if (choice is null) return;
        await RunPipeline(id, choice);
    }

    private Task ReloadGrid() => LoadOverviewAsync();

    private async Task SetFavoriteAsync(int pipelineId, bool isFavorite)
    {
        if (!_pendingFavoritePipelineIds.Add(pipelineId)) return;
        await InvokeAsync(StateHasChanged);
        try
        {
            var favorite = await Api.Pipelines.SetPipelineFavoriteAsync(pipelineId, isFavorite);
            if (favorite is null)
            {
                Toast.Error("Error", "FavoriteUpdateFailed");
                return;
            }

            if (favorite.IsFavorite)
                _favoritePipelineIds.Add(pipelineId);
            else
                _favoritePipelineIds.Remove(pipelineId);
            Toast.Success(
                favorite.IsFavorite ? "AddedToFavorites" : "RemovedFromFavorites",
                favorite.IsFavorite ? "AddedToFavorites" : "RemovedFromFavorites");
        }
        catch (HttpRequestException)
        {
            Toast.Error("Error", "FavoriteUpdateFailed");
        }
        finally
        {
            _pendingFavoritePipelineIds.Remove(pipelineId);
            await InvokeAsync(StateHasChanged);
        }
    }

    private Task ApplyFilters()
    {
        return InvokeAsync(StateHasChanged);
    }

    private Task OnTemplateFilterChanged(int? value)
    {
        _templateFilterId = value;
        SelectAvailableCatalogTab();
        if (IsGlobal)
            Nav.NavigateTo(Nav.GetUriWithQueryParameter("templateId", value));
        return ApplyFilters();
    }

    private bool MatchesTemplateFilter(PipelineDependencyDto pipeline)
    {
        if (!_templateFilterId.HasValue) return true;
        if (!_fleetItems.TryGetValue(pipeline.Id, out var fleetItem)) return false;
        return _templateFilterId == -1
            ? fleetItem.TemplateId is null && string.IsNullOrWhiteSpace(fleetItem.TemplateName)
            : fleetItem.TemplateId == _templateFilterId;
    }

    private void SelectAvailableCatalogTab()
    {
        if (FilteredParents.Count == 0 && FilteredLeaves.Count > 0)
            _catalogTabIndex = 1;
        else if (FilteredLeaves.Count == 0 && FilteredParents.Count > 0)
            _catalogTabIndex = 0;
    }

    private void OnCatalogTabChanged(int index) => _catalogTabIndex = index;

    private string FavoriteRelationIcon(PipelineDependencyDto pipeline) =>
        pipeline.References.Count > 0 ? "account_tree" : pipeline.Parents.Count > 0 ? "call_merge" : "linear_scale";

    private string FavoriteRelationText(PipelineDependencyDto pipeline) =>
        pipeline.References.Count > 0
            ? string.Format(L["ChildPipelinesCount"], pipeline.References.Count)
            : pipeline.Parents.Count > 0
                ? string.Format(L["ParentPipelinesCount"], pipeline.Parents.Count)
                : L["StandalonePipeline"];

    private PipelineFleetItemDto? FleetItem(int pipelineId) =>
        _fleetItems.GetValueOrDefault(pipelineId);

    private string PipelineHref(int id) => IsProjectScope
        ? $"/pipelines/{id}?projectId={ProjectId}"
        : IsServerScope ? $"/pipelines/{id}?serverId={ServerId}" : $"/pipelines/{id}";

    private void NavigateToPipeline(int id) => Nav.NavigateTo(PipelineHref(id));

    private void EditPipeline(int id)
    {
        var href = PipelineHref(id);
        Nav.NavigateTo($"{href}{(href.Contains('?') ? '&' : '?')}tab=edit");
    }

    private Task OnPipelineHubEvent(int pipelineId)
    {
        Logger.LogInformation("Pipeline live event received for pipeline {PipelineId}", pipelineId);
        return InvokeAsync(() => _liveCoalescer.RequestAsync(() => InvokeAsync(RefreshLiveAsync)));
    }

    private async Task OnPipelineRunEvent(int runId)
    {
        try
        {
            var run = await Api.Pipelines.GetPipelineRunAsync(runId);
            if (run is not null) await OnPipelineHubEvent(run.PipelineId);
        }
        catch (HttpRequestException) { }
    }

    // Hub-driven refresh. Global scope is server-paginated, so re-paging re-fetches; the client
    // scopes hold the full list in memory, so they must re-fetch it first to pick up new run state.
    private async Task RefreshLiveAsync()
    {
        await ReloadGrid();
        await InvokeAsync(StateHasChanged);
    }

    // --- Global scope: filters + create/import (toolbar) ---

    private async Task ClearFilters()
    {
        _search = null;
        _triggerFilter = null;
        _templateFilterId = null;
        _favoritesOnly = false;
        SelectAvailableCatalogTab();
        if (IsGlobal && TemplateId.HasValue)
            Nav.NavigateTo("/pipelines");
        await ApplyFilters();
    }

    private void NewPipeline()
    {
        var href = IsProjectScope
            ? $"/pipelines/setup?projectId={ProjectId}"
            : IsServerScope
                ? $"/pipelines/new?serverId={ServerId}"
                : "/pipelines/setup";
        Nav.NavigateTo(href);
    }

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
            var def = await Api.Packages.ValidatePipelineYamlAsync(yaml);
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
                await RefreshLiveAsync();
            });
            await _hubConnection.StartAsync();
            await _hubConnection.InvokeAsync("JoinPipelineUpdatesGroup");
            await SyncRunGroupsAsync();
            Logger.LogInformation("Pipeline live connection subscribed");
        }
        catch (Exception exception)
        {
            Logger.LogDebug(exception, "Pipeline live connection was unavailable during initial startup");
        }

        try
        {
            _entityHub = HubFactory.Create("entities");
            _entityHub.On<ResourceType, int, string>("EntityChanged", (type, id, _) =>
                type == ResourceType.Pipeline ? OnPipelineHubEvent(id) : Task.CompletedTask);
            _entityHub.RejoinOnReconnect(async () =>
            {
                await _entityHub.InvokeAsync("JoinEntityUpdates", ResourceType.Pipeline);
                await RefreshLiveAsync();
            });
            await _entityHub.StartAsync();
            await _entityHub.InvokeAsync("JoinEntityUpdates", ResourceType.Pipeline);
            Logger.LogInformation("Pipeline entity live connection subscribed");
        }
        catch (Exception exception)
        {
            Logger.LogDebug(exception, "Pipeline entity live connection was unavailable during initial startup");
        }

        _liveRecoveryTask = RecoverLiveConnectionsAsync(_liveLifetime.Token);
    }

    private async Task RecoverLiveConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                var recovered = false;
                if (_hubConnection is { State: HubConnectionState.Disconnected })
                {
                    await _hubConnection.StartAsync(ct);
                    await _hubConnection.InvokeAsync("JoinPipelineUpdatesGroup", ct);
                    _joinedRunGroups.Clear();
                    await SyncRunGroupsAsync();
                    recovered = true;
                }

                if (_entityHub is { State: HubConnectionState.Disconnected })
                {
                    await _entityHub.StartAsync(ct);
                    await _entityHub.InvokeAsync("JoinEntityUpdates", ResourceType.Pipeline, ct);
                    recovered = true;
                }

                if (recovered)
                    await InvokeAsync(RefreshLiveAsync);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception exception)
            {
                Logger.LogDebug(exception, "Pipeline live connection recovery will retry");
            }
        }
    }

    private Task OnPipelineProgressEvent() =>
        InvokeAsync(() => _liveCoalescer.RequestAsync(() => InvokeAsync(RefreshLiveAsync)));

    private async Task SyncRunGroupsAsync()
    {
        if (_hubConnection?.State != HubConnectionState.Connected) return;

        foreach (var run in _recentRuns.Where(run => run.Status is PipelineStatus.Running
                     or PipelineStatus.Pending or PipelineStatus.WaitingForApproval))
        {
            if (!_joinedRunGroups.Add(run.Id)) continue;
            try { await _hubConnection.InvokeAsync("JoinPipelineRunGroup", run.Id); }
            catch { _joinedRunGroups.Remove(run.Id); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        _liveLifetime.Cancel();
        if (_liveRecoveryTask is not null)
        {
            try { await _liveRecoveryTask; }
            catch (OperationCanceledException) { }
        }
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
        _liveLifetime.Dispose();
    }
}
