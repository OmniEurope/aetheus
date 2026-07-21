// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Radzen;
using Radzen.Blazor;
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
    [Inject] private IJSRuntime Js { get; set; } = default!;

    [Parameter] public int RunId { get; set; }
    [SupplyParameterFromQuery] public int? ProjectId { get; set; }
    [SupplyParameterFromQuery] public int? ServerId { get; set; }

    private string PipelineHref => ProjectId is { } projectId
        ? $"/pipelines/{_run!.PipelineId}?projectId={projectId}"
        : ServerId is { } serverId ? $"/pipelines/{_run!.PipelineId}?serverId={serverId}" : $"/pipelines/{_run!.PipelineId}";

    private PipelineRunDto? _run;
    private PipelineRunMetrics _metrics = new();
    private List<ReleaseDto> _releases = [];
    // The project's internal mirror repo id, resolved at load so the "Repository" tile deep-links to the
    // in-app browse page (/git-repositories/{id}) instead of the raw smart-HTTP URL the browser can't open.
    private int? _repoId;
    private List<StageViewModel> _stages = [];
    private bool _loading;
    private int _selectedTab;
    private readonly HashSet<string> _expandedMatrixGroups = [];
    // Owns the pipelines-hub connection + debounced reload + child-group joining (extracted collaborator).
    private PipelineRunLiveConnection? _live;
    private bool _rerunning;
    private PipelineRunRerunCoordinator? _rerunCoordinator;
    private bool _retrying;
    private System.Threading.Timer? _durationTimer;
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
        bool IsCurrent() => _loadSession.IsCurrent(generation) && RunId == runId;

        // Blazor keeps this component instance alive when navigating from one run route to another.
        // Tear down every run-scoped collaborator and reset the view before loading the new route;
        // otherwise the address changes while the previous run remains rendered.
        if (_live is not null)
        {
            await _live.DisposeAsync();
            _live = null;
            if (!IsCurrent()) return;
        }

        if (_logStreamer is not null)
        {
            await _logStreamer.DisposeAsync();
            _logStreamer = null;
            if (!IsCurrent()) return;
        }

        _durationTimer?.Dispose();
        _durationTimer = null;
        ResetRunState();

        _logStreamer = new PipelineRunLogStreamer(Api, HubFactory, Logger, _stepLogsCache, InvokeAsync, StateHasChanged);
        _previousDurations = new PipelineRunPreviousDurations(Api);
        _loading = true;
        PipelineRunDto? run;
        try { run = await Api.GetPipelineRunAsync(runId); }
        catch (HttpRequestException) { run = null; }
        if (!IsCurrent()) return;
        _run = run;
        if (_run is not null)
        {
            _run = NormalizeRunCompletion(_run);
            BuildStages();
            ProjectNav.Set(_run.ProjectId);

            if (_run.ProjectId is { } projectId)
            {
                int? repoId;
                try { repoId = await ResolveRepositoryIdAsync(_run, projectId); }
                catch (HttpRequestException) { repoId = null; }
                if (!IsCurrent()) return;
                _repoId = repoId;
            }

            List<ReleaseDto> releases;
            try { releases = await Api.GetReleasesByRunAsync(runId); }
            catch (HttpRequestException) { releases = []; }
            if (!IsCurrent()) return;
            _releases = releases;

            await LoadChildRunsAndAggregateAsync(ct);
            if (!IsCurrent()) return;
            await _previousDurations.LoadAsync(_childRunCache.Values.Prepend(_run));
            if (!IsCurrent()) return;

            // Never auto-select a `type: trigger` step: it has no logs of its own (it waits on a child run),
            // so selecting it would strand the log pane empty. In a trigger-only orchestration run this
            // leaves nothing selected, and the run tree invites the user to expand a child pipeline instead.
            // Ordinary and system steps (TriggeredRunId is null) are still auto-selected as before.
            var autoSelect = _stages.SelectMany(s => s.Steps)
                .FirstOrDefault(s => s.Status == TaskExecutionStatus.Running && s.TriggeredRunId is null)
                ?? _stages.SelectMany(s => s.Steps).LastOrDefault(s => s.TriggeredRunId is null && s.Status is TaskExecutionStatus.Failed or TaskExecutionStatus.Success);
            if (autoSelect is not null)
            {
                _selectedStep = autoSelect;
                if (autoSelect.TaskId.HasValue)
                {
                    try { _stepLogsCache[autoSelect.TaskId.Value] = await Api.GetTaskLogsAsync(autoSelect.TaskId.Value) ?? []; }
                    catch (HttpRequestException) { _stepLogsCache[autoSelect.TaskId.Value] = []; }
                    if (!IsCurrent()) return;
                }
            }

            await LoadFailedStepLogsAsync();
            if (!IsCurrent()) return;
            await _metrics.LoadAsync(Api, runId, _run);
            if (!IsCurrent()) return;
        }
        _loading = false;
        UpdateDurationTimer();
        await _logStreamer.UpdateAsync(_selectedStep);
        if (!IsCurrent()) return;

        _live = new PipelineRunLiveConnection(HubFactory, Logger, runId, () => _childRunCache.Keys, ReloadRunAsync, InvokeAsync);
        await _live.StartAsync();
    }

    private void ResetRunState()
    {
        _run = null;
        _metrics = new PipelineRunMetrics();
        _releases = [];
        _repoId = null;
        _stages = [];
        _selectedTab = 0;
        _expandedMatrixGroups.Clear();
        _childRunCache.Clear();
        _stepLogsCache.Clear();
        _selectedStep = null;
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

        var (children, merged) = await PipelineRunChildAggregator.LoadAsync(_run, Api, ct);
        foreach (var kv in children) _childRunCache[kv.Key] = kv.Value;
        _run = merged;
    }

    private void BuildStages()
    {
        if (_run is null) return;
        _logVarRegex = PipelineRunLogView.BuildLogVarRegex(_run);
        _stages = RunStageBuilder.Build(_run);
    }

    private async Task ReloadRunAsync()
    {
        _run = await Api.GetPipelineRunAsync(RunId);
        if (_run is not null)
        {
            _run = NormalizeRunCompletion(_run);
            UpdateDurationTimer();
            BuildStages();
            try { _releases = await Api.GetReleasesByRunAsync(RunId); }
            catch (HttpRequestException) { /* keep prior releases on a transient failure */ }
            await LoadChildRunsAndAggregateAsync(_loadSession.Token);
            await _previousDurations.LoadAsync(_childRunCache.Values.Prepend(_run));
            // A child run may have just appeared (its trigger step fired) - join its group so its own
            // step events keep this view live from here on.
            if (_live is not null) await _live.JoinGroupsAsync();
            // Follow execution task-to-task, including into a triggered child run: prefer a running step
            // that actually has logs (a real TaskId) over the logless trigger step, searching the parent
            // and every child. Falls back to any running step so a single run behaves exactly as before.
            var runningStep = AllStepsAcrossChildren().FirstOrDefault(s => s.Status == TaskExecutionStatus.Running && s.TaskId is not null)
                ?? _stages.SelectMany(s => s.Steps).FirstOrDefault(s => s.Status == TaskExecutionStatus.Running);
            if (runningStep is not null)
            {
                _selectedStep = runningStep;
                // First time we follow this task, seed its history (the log hub streams only NEW lines from
                // the join point, it does not replay). Skip if already cached - the streamer keeps it fresh.
                if (runningStep.TaskId is { } tid && !_stepLogsCache.ContainsKey(tid))
                {
                    try { _stepLogsCache[tid] = await Api.GetTaskLogsAsync(tid) ?? []; }
                    catch (HttpRequestException) { _stepLogsCache[tid] = []; }
                }
            }
            await LoadFailedStepLogsAsync();
        }
        else
        {
            UpdateDurationTimer();
        }
        if (_logStreamer is not null) await _logStreamer.UpdateAsync(_selectedStep);
        StateHasChanged();
    }

    private Task<int?> ResolveRepositoryIdAsync(PipelineRunDto run, int projectId) =>
        PipelineRunRepositoryResolver.ResolveAsync(Api, run, projectId);

    private void StartTickTimer()
    {
        _durationTimer ??= new System.Threading.Timer(
            _ => _ = InvokeAsync(StateHasChanged),
            null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void UpdateDurationTimer()
    {
        if (RunInProgress)
        {
            StartTickTimer();
            return;
        }

        _durationTimer?.Dispose();
        _durationTimer = null;
    }

    // Every step across the parent run and its loaded child runs, so the log pane can follow the running
    // task INTO a triggered child (a trigger step itself has no TaskId, hence no logs of its own).
    private IEnumerable<PipelineStepRunDto> AllStepsAcrossChildren()
    {
        foreach (var step in _stages.SelectMany(s => s.Steps)) yield return step;
        foreach (var child in _childRunCache.Values)
            foreach (var step in child.Steps) yield return step;
    }

    public async ValueTask DisposeAsync()
    {
        _loadSession.Dispose();

        if (_live is not null)
            await _live.DisposeAsync();

        if (_logStreamer is not null)
            await _logStreamer.DisposeAsync();

        _durationTimer?.Dispose();
    }

    private void ToggleMatrixGroup(string key)
    {
        if (!_expandedMatrixGroups.Remove(key))
            _expandedMatrixGroups.Add(key);
    }

    private string? GitBranch => _run?.ResolvedVariables
        .GetValueOrDefault("BUILD_SOURCEBRANCH")
        ?? _run?.ResolvedVariables.GetValueOrDefault("DEFAULT_BRANCH");

    private IReadOnlyList<string> BlockingWarnings =>
        _run is null ? [] : _run.Warnings.Where(IsBlockingWarning).ToList();

    private IReadOnlyList<string> InfoWarnings =>
        _run is null ? [] : _run.Warnings.Where(w => !IsBlockingWarning(w)).ToList();

    private IReadOnlyList<BlockingWarningView> BlockingWarningViews =>
        _run is null ? [] : _run.Warnings.Where(IsBlockingWarning).Select(ToBlockingView).ToList();

    private bool HasFailedSteps => _run?.Steps.Any(s => s.Status == TaskExecutionStatus.Failed) == true;

    private bool CanRerun => PipelineRunRerunCoordinator.CanRerun(_run);

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

    private List<string> TabSlugs() => PipelineRunPresentation.TabSlugs(_run, HasYamlTab, HasLintTab);

    // Overview cross-link tiles that jump into a tab. They navigate (push history) so the URL stays the
    // source of truth for UrlSyncedTabs; the guard against the current tab avoids duplicate entries.
    private void NavigateToTab(string slug)
    {
        var idx = TabSlugs().IndexOf(slug);
        if (idx < 0 || idx == _selectedTab) return;
        Nav.NavigateTo(Nav.GetUriWithQueryParameter("tab", idx == 0 ? null : slug));
    }

    private void GoToLogsTab() => NavigateToTab("logs");
    private void GoToCoverageTab() => NavigateToTab("coverage");
    private void GoToLintTab() => NavigateToTab("lint");

    /// <summary>True when lint passed; false if it failed; null when there is no lint data.
    /// Structured SARIF data (no errors) wins when present, falling back to the step-status heuristic.</summary>
    private bool? LintPassed => _run?.LintSummary is { } summary
        ? summary.Passed
        : (LintSteps.Count > 0 ? LintSteps.All(s => s.Status != TaskExecutionStatus.Failed) : null);

    /// <summary>The repository URL only when it is a real http(s) link - guards against ssh-style
    /// URLs (git@host:…) that the Blazor router would otherwise try to navigate internally and crash on.</summary>
    private string? SafeRepoUrl => IsWebUrl(_run?.RepositoryUrl) ? _run!.RepositoryUrl : null;

    /// <summary>External commit URL ({repo}/commit/{sha}) when the repository is a real http(s) link.</summary>
    private string? CommitUrl => BuildCommitUrl(_run?.RepositoryUrl, _run?.CommitHash);

    // Lint steps of this run PLUS those of any triggered child run (orchestration bubble-up): a
    // parent orchestration run has no lint steps of its own, so surface the children's here so the
    // Lint tab + overview tile appear on the parent too (structured LintSummary bubbles separately).
    private IReadOnlyList<PipelineStepRunDto> LintSteps =>
        (_run?.Steps ?? Enumerable.Empty<PipelineStepRunDto>())
            .Concat(_childRunCache.Values.SelectMany(c => c.Steps))
            .Where(s => !s.IsSystem && (
                s.StepName.Contains("lint", StringComparison.OrdinalIgnoreCase) ||
                s.StageName.Contains("lint", StringComparison.OrdinalIgnoreCase)))
            .ToList();

    private async Task LoadLintLogsAsync(int taskId)
    {
        try { _stepLogsCache[taskId] = await Api.GetTaskLogsAsync(taskId) ?? []; }
        catch (HttpRequestException) { _stepLogsCache[taskId] = []; }
        StateHasChanged();
    }

    private async Task LoadCoverageLogsAsync(int taskId)
    {
        try { _stepLogsCache[taskId] = await Api.GetTaskLogsAsync(taskId) ?? []; }
        catch (HttpRequestException) { _stepLogsCache[taskId] = []; }
        StateHasChanged();
    }

    private async Task RetryFailedAsync()
    {
        if (_run is null || _retrying) return;
        _retrying = true;
        try
        {
            var run = await Api.RetryFailedStepsAsync(_run.Id);
            if (run is not null)
                await ReloadRunAsync();
            else
                Toast.Error("PipelineRunFailed", "Error");
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
        var confirmed = await Dialog.Confirm(
            L["CancelRunConfirm"].Value, L["CancelRun"].Value,
            new ConfirmOptions { OkButtonText = L["CancelRun"].Value, CancelButtonText = L["Back"].Value });
        if (confirmed != true) return;

        _cancelling = true;
        try
        {
            var status = await Api.CancelPipelineRunAsync(_run.Id);
            if (status.Success)
                Toast.Info("RunCancelled", "RunCancelledDetail");
            else if (status.Forbidden)
                Toast.Error("PipelineRunFailed", "PipelineRunForbidden");
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

    private async Task ShowVariables()
    {
        if (_run is null || _run.ResolvedVariables.Count == 0) return;
        await Dialog.OpenAsync<PipelineRunVariablesDialog>(
            string.Format(L["VarsCount"], _run.ResolvedVariables.Count),
            new Dictionary<string, object?> { { "Variables", _run.ResolvedVariables } },
            new DialogOptions { Width = "600px" });
    }

    private string GetNextConnectorStatus(int stageIndex) =>
        PipelineRunPresentation.NextConnectorStatus(_stages, stageIndex);

    // --- Step log viewer (split view) ---
    private PipelineStepRunDto? _selectedStep;
    private string _logSearch = string.Empty;

    // The executed command can echo secrets/long argv, so it is masked by default and revealed on
    // demand; the output (results) is shown by default but can be collapsed to focus on the command.
    private bool _showCommand;
    private bool _showResults = true;

    // S-UX-17: copy the selected step's full log to the clipboard.
    private async Task CopyLogsAsync()
    {
        if (_selectedStep?.TaskId is not { } taskId) return;
        var logs = _stepLogsCache.GetValueOrDefault(taskId);
        if (logs is null or { Count: 0 }) return;
        await Js.InvokeVoidAsync("Aetheus.copyToClipboard", string.Join("\n", logs.Select(l => l.Message)));
        Toast.Success("LogsCopied");
    }

    // S-UX-RUNB: one-line "why did it fail" summary for the top-of-page banner (failed step names + exit codes).
    private string FailedRunReason => string.Join(", ", FailedSteps.Select(s =>
        s.ExitCode is { } code ? string.Format(L["FailedStepWithExit"], s.StepName, code) : s.StepName));

    // S-UX-28: failed non-system steps whose error log should be surfaced inline on the Overview tab.
    private IReadOnlyList<PipelineStepRunDto> FailedSteps =>
        _run?.Steps.Where(s => s.Status == TaskExecutionStatus.Failed && !s.IsSystem).ToList() ?? [];

    // Preload the logs for every failed step so the Overview error card can render them without a click.
    private Task LoadFailedStepLogsAsync() =>
        PipelineRunLogView.LoadFailedStepLogsAsync(FailedSteps, Api, _stepLogsCache);

    // S-UX-29: stream-download an artifact directly from the run's Artifacts tab (no detail-page hop).
    private async Task DownloadArtifactAsync(PipelineArtifactDto artifact)
    {
        var stream = await Api.DownloadArtifactAsync(artifact.Id);
        if (stream is null) return;
        using var streamRef = new DotNetStreamReference(stream);
        await Js.InvokeVoidAsync("downloadFileFromStream", $"{artifact.Name}.zip", streamRef);
    }

    // S-UX-32: copy the executed YAML snapshot to the clipboard.
    private async Task CopyYamlAsync()
    {
        if (string.IsNullOrEmpty(_run?.YamlSnapshot)) return;
        await Js.InvokeVoidAsync("Aetheus.copyToClipboard", _run.YamlSnapshot);
        Toast.Success("Copied");
    }

    // YAML definition peek: a `pipeline: <name>` line in the YAML tab is clickable; setting _peekName opens
    // the referenced pipeline's live definition in the shared read-only <PipelineDefinitionPeek> right pane.
    private string? _peekName;
    // Toggle soft-wrapping of long YAML lines on the left (definition) pane (the peek pane has its own).
    private bool _yamlWrap;

    private void ClosePeek() => _peekName = null;
    private readonly Dictionary<int, List<TaskLogDto>> _stepLogsCache = [];
    private bool _logsLoading;

    private async Task SelectStep(PipelineStepRunDto step)
    {
        _selectedStep = step;
        _lastAutoScrollCount = -1; // re-pin auto-follow to the newly selected step's tail
        NavigateToTab("logs");
        if (_logStreamer is not null) await _logStreamer.UpdateAsync(_selectedStep);

        if (step.TaskId is null) return;
        var taskId = step.TaskId.Value;

        if (step.Status == TaskExecutionStatus.Running)
            _stepLogsCache.Remove(taskId);

        if (_stepLogsCache.ContainsKey(taskId)) return;

        _logsLoading = true;
        StateHasChanged();
        try
        {
            _stepLogsCache[taskId] = await Api.GetTaskLogsAsync(taskId) ?? [];
        }
        catch (HttpRequestException)
        {
            _stepLogsCache[taskId] = [];
        }
        _logsLoading = false;
    }

}
