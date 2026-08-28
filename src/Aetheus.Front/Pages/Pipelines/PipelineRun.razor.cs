// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;
using static Aetheus.Front.Pages.Pipelines.PipelineRunFormatting;

namespace Aetheus.Front.Pages.Pipelines;

public partial class PipelineRun : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] private ILogger<PipelineRun> Logger { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private ProjectNavContextService ProjectNav { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Parameter] public int RunId { get; set; }
    [SupplyParameterFromQuery] public int? ProjectId { get; set; }
    [SupplyParameterFromQuery] public int? ServerId { get; set; }
    private int? EffectiveProjectId => _run?.ProjectId ?? ProjectId;

    private string PipelineHref => EffectiveProjectId is { } projectId
        ? $"/pipelines/{_run!.PipelineId}?projectId={projectId}"
        : ServerId is { } serverId ? $"/pipelines/{_run!.PipelineId}?serverId={serverId}" : $"/pipelines/{_run!.PipelineId}";
    private PipelineRunDto? _run;
    private PipelineRunMetrics _metrics = new();
    private readonly PipelineRunGateState _gateState = new();
    private readonly PipelineRunPageCache _pageCache = new();
    private List<ReleaseDto> _releases = [];
    private List<AiRunResultDto> _aiResults = [];
    // Internal mirror id used by the Repository tile instead of the raw smart-HTTP URL.
    private int? _repoId;
    private List<StageViewModel> _stages = [];
    private bool _loading;
    private int _selectedTab;
    private readonly HashSet<string> _expandedMatrixGroups = [];
    // Owns the pipelines-hub connection + debounced reload + child-group joining (extracted collaborator).
    private PipelineRunLiveConnection? _live;
    private bool _rerunning;
    private PipelineRunRerunCoordinator? _rerunCoordinator;
    private PipelineRunCommandCoordinator? _commandCoordinator;
    private bool _retrying;
    // S-TECH-32: live log streaming for the selected running step (poll stays as fallback). Owned by
    // the log streamer collaborator; the component keeps the shared log cache it writes into.
    private PipelineRunLogStreamer? _logStreamer;
    private PipelineRunPreviousDurations _previousDurations = default!;
    private int? _loadedRunId;
    private readonly PipelineRunLoadSession _loadSession = new();

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedRunId == RunId) return;

        _loadedRunId = RunId;
        var load = _loadSession.Begin();
        try { await LoadRunPageAsync(RunId, load.Generation, load.Token); }
        catch (OperationCanceledException) when (load.Token.IsCancellationRequested) { }
    }

    private async Task LoadRunPageAsync(int runId, int generation, CancellationToken ct)
    {
        if (!await ResetRunPageAsync(runId, generation).ConfigureAwait(false)) return;
        _logStreamer = new PipelineRunLogStreamer(Api, HubFactory, Logger, _stepLogsCache, InvokeAsync, StateHasChanged);
        _previousDurations = new PipelineRunPreviousDurations(Api);
        _loading = true;
        _run = await LoadRootRunAsync(runId, ct).ConfigureAwait(false);
        if (!IsCurrentLoad(runId, generation)) return;
        if (_run is not null)
            if (!await EnrichRunAsync(runId, generation, ct).ConfigureAwait(false)) return;
        _loading = false;
        await _logStreamer.UpdateAsync(_selectedStep);
        if (!IsCurrentLoad(runId, generation)) return;
        _live = new PipelineRunLiveConnection(HubFactory, Logger, runId,
            () => LiveChildRunIds(_childRunCache), ReloadRunAsync, RefreshQueueAsync, InvokeAsync,
            () => AllStepsAcrossChildren().Any(step => step.Status == TaskExecutionStatus.Assigned));
        await _live.StartAsync();
    }

    private bool IsCurrentLoad(int runId, int generation) =>
        _loadSession.IsCurrent(generation) && RunId == runId;

    private void ReassertBreadcrumb()
    {
        if (_run is null) return;
        var runItem = new BreadcrumbItem($"{L["PipelineRun"]} #{_run.Id}");
        if (_run.ProjectId is { } projectId)
        {
            Breadcrumb.Set(
                new BreadcrumbItem(L["Projects"], "/projects"),
                new BreadcrumbItem(_run.ProjectName ?? $"{L["Project"]} #{projectId}", $"/projects/{projectId}/overview"),
                new BreadcrumbItem(L["Pipelines"], $"/projects/{projectId}/pipelines"),
                new BreadcrumbItem(_run.PipelineName, PipelineHref),
                runItem);
            return;
        }

        Breadcrumb.Set(
            new BreadcrumbItem(L["Pipelines"], "/pipelines"),
            new BreadcrumbItem(_run.PipelineName, PipelineHref),
            runItem);
    }

    private async Task<bool> ResetRunPageAsync(int runId, int generation)
    {
        if (_live is not null)
        {
            await _live.DisposeAsync();
            _live = null;
            if (!IsCurrentLoad(runId, generation)) return false;
        }
        if (_logStreamer is not null)
        {
            await _logStreamer.DisposeAsync();
            _logStreamer = null;
            if (!IsCurrentLoad(runId, generation)) return false;
        }
        ResetRunState();
        return true;
    }

    private async Task<PipelineRunDto?> LoadRootRunAsync(int runId, CancellationToken ct)
    {
        try
        {
            return _pageCache.TryGetRun(runId, out var cached)
                ? cached
                : await Api.Pipelines.GetPipelineRunAsync(runId, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<bool> EnrichRunAsync(int runId, int generation, CancellationToken ct)
    {
        _run = NormalizeRunCompletion(_run!);
        _pageCache.StoreRun(_run);
        BuildStages();
        ProjectNav.Set(_run.ProjectId);
        ReassertBreadcrumb();
        var rootRun = _run;
        var repositoryTask = PipelineRunRepositoryResolver.ResolveAsync(Api, rootRun, ct);
        var releasesTask = Api.Projects.GetReleasesByRunAsync(runId, ct);
        var aiResultsTask = PipelineRunAiResults.LoadAsync(Api, runId, ct);
        var failedLogsTask = LoadFailedStepLogsAsync();
        var (children, mergedRun) = await PipelineRunChildAggregator.LoadAsync(rootRun, Api, _pageCache, ct);
        if (!IsCurrentLoad(runId, generation)) return false;
        _childRunCache.Clear();
        foreach (var child in children) _childRunCache[child.Key] = child.Value;
        _run = mergedRun;
        await Task.WhenAll(
            _previousDurations.LoadAsync(_childRunCache.Values.Prepend(_run)),
            LoadGateAsync(ct),
            _metrics.LoadAsync(Api, runId, _run),
            failedLogsTask);
        if (!IsCurrentLoad(runId, generation)) return false;
        _repoId = await repositoryTask;
        _releases = await LoadReleasesAsync(releasesTask).ConfigureAwait(false);
        _aiResults = await aiResultsTask;
        return await AutoSelectStepAsync(runId, generation).ConfigureAwait(false);
    }

    private static async Task<List<ReleaseDto>> LoadReleasesAsync(Task<List<ReleaseDto>> releasesTask)
    {
        try { return await releasesTask; }
        catch (HttpRequestException) { return []; }
    }

    private async Task<bool> AutoSelectStepAsync(int runId, int generation)
    {
        var autoSelect = _stages.SelectMany(s => s.Steps)
            .FirstOrDefault(s => s.Status == TaskExecutionStatus.Running && s.TriggeredRunId is null)
            ?? _stages.SelectMany(s => s.Steps)
                .LastOrDefault(s => s.TriggeredRunId is null
                                    && s.Status is TaskExecutionStatus.Failed or TaskExecutionStatus.Success);
        if (autoSelect is null) return true;
        _selectedStep = autoSelect;
        await _logStreamer!.UpdateAsync(_selectedStep);
        if (!IsCurrentLoad(runId, generation)) return false;
        if (!autoSelect.TaskId.HasValue) return true;
        await LoadTaskLogSnapshotAsync(autoSelect.TaskId.Value);
        return IsCurrentLoad(runId, generation);
    }

    private void ResetRunState()
    {
        _run = null;
        _metrics = new PipelineRunMetrics();
        _gateState.Clear();
        _releases = [];
        _aiResults = [];
        _repoId = null;
        _stages = [];
        _selectedTab = 0;
        _expandedMatrixGroups.Clear();
        _childRunCache.Clear();
        _stepLogsCache.Clear();
        _selectedStep = null;
        _userPinnedStep = false;
        _logVarRegex = null;
        _logSearch = string.Empty;
        _showCommand = false;
        _showResults = true;
        _peekName = null;
        _yamlWrap = false;
        _logsLoading = false;
        _followLogs = true;
        _lastAutoScrollCount = -1;
        _rerunning = false;
        _retrying = false;
        _cancelling = false;
    }

    // S-DES-11: regex over the run's known variable names (built by PipelineRunLogView) so they can be
    // accented inside log lines; consumed by the markup via PipelineRunLogView.HighlightLogVariables.
    private Regex? _logVarRegex;

    /// <summary>A run is genuinely "in progress" only when it has no end timestamp AND a non-terminal
    /// status - so an orphaned run (terminal status, missing CompletedAt) is not treated as live.</summary>
    private bool RunInProgress => _run is not null && _run.CompletedAt is null && !IsTerminal(_run.Status);

    /// <summary>A run is an orchestration when at least one step is a <c>type: trigger</c> child launcher
    /// (<see cref="PipelineStepRunDto.TriggeredRunId"/>). Its Journaux tab drops the 22rem split for a
    /// full-width run tree (the mockup layout): trigger nodes have no logs of their own, so the tree - not
    /// a log pane - is the primary content.</summary>
    private bool IsOrchestration => _run?.Steps.Any(s => s.TriggeredRunId is not null) == true;

    // Orchestration: the triggered child runs, preloaded at load time (keyed by run id). Used to (a) bubble
    // the children's coverage/lint/test/complexity data up onto this run's tabs+tiles, and (b) let the run
    // timeline expand instantly and reveal a selected child step without a per-click fetch.
    private readonly Dictionary<int, PipelineRunDto> _childRunCache = [];
    private IReadOnlyDictionary<int, PipelineRunDto> ChildRuns => _childRunCache;

    /// <summary>Fetch the triggered child runs and merge their result data onto <c>_run</c> so an
    /// orchestration run surfaces the same Coverage/Lint/Code Quality/Test Results tabs and overview tiles
    /// as the child pipelines that actually produced them. Delegated to <see cref="PipelineRunChildAggregator"/>.</summary>
    private async Task LoadChildRunsAndAggregateAsync(CancellationToken ct = default)
    {
        _childRunCache.Clear();
        if (_run is null || !IsOrchestration) return;

        var (children, merged) = await PipelineRunChildAggregator.LoadAsync(_run, Api, _pageCache, ct);
        foreach (var kv in children) _childRunCache[kv.Key] = kv.Value;
        _run = merged;
    }

    private void BuildStages()
    {
        if (_run is null) return;
        _logVarRegex = PipelineRunLogView.BuildLogVarRegex(_run);
        _stages = RunStageBuilder.Build(_run);
    }

    internal async Task ReloadRunAsync()
    {
        _run = await Api.Pipelines.GetPipelineRunAsync(RunId);
        if (_run is not null)
        {
            _run = NormalizeRunCompletion(_run);
            BuildStages();
            try { _releases = await Api.Projects.GetReleasesByRunAsync(RunId); }
            catch (HttpRequestException) { /* keep prior releases on a transient failure */ }
            await LoadChildRunsAndAggregateAsync(_loadSession.Token);
            await _previousDurations.LoadAsync(_childRunCache.Values.Prepend(_run));
            await LoadGateAsync(_loadSession.Token);
            // A child run may have just appeared (its trigger step fired) - join its group so its own
            // step events keep this view live from here on.
            if (_live is not null) await _live.JoinGroupsAsync();
            // A manual selection is sticky across live reloads. Refresh the selected DTO by its persisted
            // step id, but never replace it with a different running parent/child step. If the selected
            // step disappeared (for example after a run retry rebuilt its steps), release the pin and
            // resume automatic execution following.
            var pinnedStep = _userPinnedStep && _selectedStep is not null
                ? AllStepsAcrossChildren().FirstOrDefault(step => step.Id == _selectedStep.Id)
                : null;
            if (pinnedStep is not null)
            {
                _selectedStep = pinnedStep;
            }
            else
            {
                _userPinnedStep = false;
                // Follow execution task-to-task, including into a triggered child run: prefer a running
                // step that actually has logs over the logless trigger step.
                var runningStep = AllStepsAcrossChildren().FirstOrDefault(step =>
                                      step.Status == TaskExecutionStatus.Running && step.TaskId is not null)
                                  ?? _stages.SelectMany(stage => stage.Steps).FirstOrDefault(step =>
                                      step.Status == TaskExecutionStatus.Running);
                if (runningStep is not null)
                {
                    _selectedStep = runningStep;
                    if (_logStreamer is not null) await _logStreamer.UpdateAsync(_selectedStep);
                    // First time we follow this task, seed its history (the log hub streams only NEW lines
                    // from the join point, it does not replay).
                    if (runningStep.TaskId is { } tid && !_stepLogsCache.ContainsKey(tid))
                    {
                        await LoadTaskLogSnapshotAsync(tid);
                    }
                }
            }
            await LoadFailedStepLogsAsync();
        }
        else
        {
        }
        if (_logStreamer is not null) await _logStreamer.UpdateAsync(_selectedStep);
        StateHasChanged();
    }

    internal async Task RefreshQueueAsync()
    {
        if (_run is null) return;
        _run = await PipelineRunQueueRefresh.RefreshAsync(
            Api, _run, _childRunCache, _loadSession.Token);
        BuildStages();
        if (_selectedStep is not null)
            _selectedStep = AllStepsAcrossChildren().FirstOrDefault(step => step.Id == _selectedStep.Id)
                ?? _selectedStep;
        StateHasChanged();
    }

    private IEnumerable<PipelineStepRunDto> AllStepsAcrossChildren() =>
        PipelineRunPresentation.AllSteps(_stages, _childRunCache);

    internal static IReadOnlyCollection<int> LiveChildRunIds(IReadOnlyDictionary<int, PipelineRunDto> children) =>
        PipelineRunPresentation.LiveChildRunIds(children);

    public async ValueTask DisposeAsync()
    {
        _loadSession.Dispose();

        if (_live is not null)
            await _live.DisposeAsync();

        if (_logStreamer is not null)
            await _logStreamer.DisposeAsync();

    }

    private void ToggleMatrixGroup(string key)
    {
        if (!_expandedMatrixGroups.Remove(key))
            _expandedMatrixGroups.Add(key);
    }

    private string? GitBranch => PipelineRunPresentation.GitBranch(_run);

    /// <summary>Application version this run built (APP_VERSION), shown under the Release tile.</summary>
    private string? AppVersion => _run is null ? null : BuiltAppVersion(_run);

    private IReadOnlyList<string> BlockingWarnings =>
        _run is null ? [] : _run.Warnings.Where(IsBlockingWarning).ToList();

    private IReadOnlyList<string> InfoWarnings =>
        _run is null ? [] : _run.Warnings.Where(w => !IsBlockingWarning(w)).ToList();

    private IReadOnlyList<BlockingWarningView> BlockingWarningViews =>
        _run is null ? [] : _run.Warnings.Where(IsBlockingWarning).Select(ToBlockingView).ToList();

    private bool HasFailedSteps => _run?.Steps.Any(s => s.Status == TaskExecutionStatus.Failed) == true;

    private bool CanRerun => PipelineRunRerunCoordinator.CanRerun(_run);

    private PipelineRunCommandCoordinator Commands =>
        _commandCoordinator ??= new(Api, Dialog, Toast, L, Js);

    private async Task OnRerunClick(RadzenSplitButtonItem? item)
    {
        if (_run is null || _rerunning) return;

        _rerunning = true;
        try
        {
            _rerunCoordinator ??= new PipelineRunRerunCoordinator(Api, Nav, Toast, Dialog, L);
            await _rerunCoordinator.RerunAsync(_run, item);
        }
        finally
        {
            _rerunning = false;
        }
    }

    private bool HasLintTab => _run?.LintSummary is not null || LintSteps.Count > 0;

    private bool HasYamlTab => !string.IsNullOrEmpty(_run?.YamlSnapshot);

    private bool ShowGateTab => !RunInProgress && _gateState.HasResult;

    private List<string> TabSlugs() => PipelineRunPresentation.TabSlugs(_run, ShowGateTab, HasYamlTab, HasLintTab);

    // Overview cross-link tiles that jump into a tab. They navigate (push history) so the URL stays the
    // source of truth for UrlSyncedTabs; the guard against the current tab avoids duplicate entries.
    private void NavigateToTab(string slug)
    {
        var idx = TabSlugs().IndexOf(slug);
        if (idx < 0 || idx == _selectedTab) return;
        Nav.NavigateTo(Nav.GetUriWithQueryParameter("tab", idx == 0 ? null : slug));
    }

    private void GoToLogsTab() => NavigateToTab("logs");
    private void GoToArtifactsTab() => NavigateToTab("artifacts");
    private void GoToCoverageTab() => NavigateToTab("coverage");
    private void GoToLintTab() => NavigateToTab("lint");

    private Task LoadGateAsync(CancellationToken ct)
    {
        if (_run is null || RunInProgress)
        {
            _gateState.Clear();
            return Task.CompletedTask;
        }

        return _gateState.LoadAsync(Api, _run, _childRunCache.Values, _pageCache, ct);
    }
    private bool? LintPassed => PipelineRunPresentation.LintPassed(_run, LintSteps);

    private IReadOnlyList<PipelineStepRunDto> LintSteps =>
        PipelineRunPresentation.LintSteps(_run, _childRunCache);

    private async Task LoadLintLogsAsync(int taskId)
    {
        try { _stepLogsCache[taskId] = await Api.Monitoring.GetTaskLogsAsync(taskId) ?? []; }
        catch (HttpRequestException) { _stepLogsCache[taskId] = []; }
        StateHasChanged();
    }

    private async Task LoadCoverageLogsAsync(int taskId)
    {
        try { _stepLogsCache[taskId] = await Api.Monitoring.GetTaskLogsAsync(taskId) ?? []; }
        catch (HttpRequestException) { _stepLogsCache[taskId] = []; }
        StateHasChanged();
    }

    private async Task RetryFailedAsync()
    {
        if (_run is null || _retrying) return;
        _retrying = true;
        try
        {
            if (await Commands.RetryFailedAsync(_run))
                await ReloadRunAsync();
        }
        finally
        {
            _retrying = false;
        }
    }

    private bool _cancelling;

    // Cancel an in-progress run (kills its running steps). Confirm first, then reload so the UI reflects
    // the Cancelled state. A run that finished server-side in the meantime comes back NotFound - reload
    // anyway so the view catches up to the terminal state.
    private async Task CancelRunAsync()
    {
        if (_run is null || _cancelling || !RunInProgress) return;
        if (!await Commands.ConfirmCancellationAsync()) return;
        _cancelling = true;
        try
        {
            await Commands.CancelAsync(_run);
            await ReloadRunAsync();
        }
        finally
        {
            _cancelling = false;
        }
    }

    // --- Log scrolling / auto-follow (S: live test runs emit thousands of lines) ---
    private ElementReference _logsTerminal;
    // Auto-follow keeps the terminal pinned to the newest line while a step streams; the user turns it
    // off by scrolling up via the "jump to top" control, and back on with "jump to bottom" / the toggle.
    private bool _followLogs = true;
    // Last streamed line count we auto-scrolled for, so OnAfterRender only re-pins when new lines arrived
    // (not on every unrelated re-render such as the 1s duration tick). Reset to -1 forces the next pin.
    private int _lastAutoScrollCount = -1;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_followLogs || _selectedStep?.TaskId is not { } taskId) return;
        var count = _stepLogsCache.GetValueOrDefault(taskId)?.Count ?? 0;
        if (count == _lastAutoScrollCount) return;
        _lastAutoScrollCount = count;
        try { await Js.InvokeVoidAsync("Aetheus.scrollToBottom", _logsTerminal); }
        catch (JSException) { /* terminal not mounted (e.g. logs collapsed) - nothing to scroll */ }
    }

    private async Task ScrollLogsToTopAsync()
    {
        _followLogs = false; // jumping to the top means the user wants to read history, not follow the tail
        try { await Js.InvokeVoidAsync("Aetheus.scrollToTop", _logsTerminal); }
        catch (JSException) { }
    }

    private async Task ScrollLogsToBottomAsync()
    {
        _followLogs = true;
        _lastAutoScrollCount = -1;
        try { await Js.InvokeVoidAsync("Aetheus.scrollToBottom", _logsTerminal); }
        catch (JSException) { }
    }

    private async Task ToggleFollowLogs()
    {
        _followLogs = !_followLogs;
        if (_followLogs)
        {
            _lastAutoScrollCount = -1;
            try { await Js.InvokeVoidAsync("Aetheus.scrollToBottom", _logsTerminal); }
            catch (JSException) { }
        }
    }

    private Task ShowVariables() =>
        _run is null || _run.ResolvedVariables.Count == 0
            ? Task.CompletedTask
            : Commands.ShowVariablesAsync(_run);

    private Task ShowParameters() =>
        _run is null || _run.Parameters.Count == 0
            ? Task.CompletedTask
            : Commands.ShowParametersAsync(_run);

    private string GetNextConnectorStatus(int stageIndex) =>
        PipelineRunPresentation.NextConnectorStatus(_stages, stageIndex);

    // --- Step log viewer (split view) ---
    private PipelineStepRunDto? _selectedStep;
    private bool _userPinnedStep;
    internal PipelineStepRunDto? SelectedStep => _selectedStep;
    internal bool IsStepSelectionPinned => _userPinnedStep;
    private string _logSearch = string.Empty;

    // The executed command can echo secrets/long argv, so it is masked by default and revealed on
    // demand; the output (results) is shown by default but can be collapsed to focus on the command.
    private bool _showCommand;
    private bool _showResults = true;

    // S-UX-17: copy the selected step's full log to the clipboard.
    private Task CopyLogsAsync() =>
        _selectedStep?.TaskId is { } taskId
        && _stepLogsCache.GetValueOrDefault(taskId) is { Count: > 0 } logs
            ? Commands.CopyLogsAsync(logs)
            : Task.CompletedTask;

    private string FailedRunReason => string.Join(", ", FailedSteps.Select(step =>
        step.ExitCode is { } code ? string.Format(L["FailedStepWithExit"], step.StepName, code) : step.StepName));
    private IReadOnlyList<PipelineStepRunDto> FailedSteps =>
        _run?.Steps.Where(step => step.Status == TaskExecutionStatus.Failed && !step.IsSystem).ToList() ?? [];
    private Task LoadFailedStepLogsAsync() => PipelineRunLogView.LoadFailedStepLogsAsync(FailedSteps, Api, _stepLogsCache);
    private Task DownloadArtifactAsync(PipelineArtifactDto artifact) => Commands.DownloadArtifactAsync(artifact);
    private Task OpenAiResultAsync(AiRunResultDto result) => PipelineRunAiResults.OpenAsync(Dialog, L, result);
    private Task CopyYamlAsync() => string.IsNullOrEmpty(_run?.YamlSnapshot) ? Task.CompletedTask : Commands.CopyYamlAsync(_run.YamlSnapshot);
    private string? _peekName;
    private bool _yamlWrap;
    private void ClosePeek() => _peekName = null;
    private readonly Dictionary<int, List<TaskLogDto>> _stepLogsCache = [];
    private bool _logsLoading;
    internal async Task SelectStep(PipelineStepRunDto step)
    {
        _selectedStep = step;
        _userPinnedStep = true;
        _lastAutoScrollCount = -1;
        NavigateToTab("logs");
        if (step.TaskId is null)
        {
            if (_logStreamer is not null) await _logStreamer.UpdateAsync(_selectedStep);
            return;
        }
        var taskId = step.TaskId.Value;
        if (step.Status == TaskExecutionStatus.Running) _stepLogsCache.Remove(taskId);
        if (_logStreamer is not null) await _logStreamer.UpdateAsync(_selectedStep);
        if (_stepLogsCache.ContainsKey(taskId)) return;
        _logsLoading = true;
        StateHasChanged();
        await LoadTaskLogSnapshotAsync(taskId);
        _logsLoading = false;
    }
    private async Task LoadTaskLogSnapshotAsync(int taskId)
    {
        List<TaskLogDto> snapshot;
        try { snapshot = await Api.Monitoring.GetTaskLogsAsync(taskId) ?? []; }
        catch (HttpRequestException) { snapshot = []; }
        _stepLogsCache.TryGetValue(taskId, out var streamedDuringSnapshot);
        _stepLogsCache[taskId] = PipelineRunLogSnapshot.Merge(snapshot, streamedDuringSnapshot ?? []);
    }
}
