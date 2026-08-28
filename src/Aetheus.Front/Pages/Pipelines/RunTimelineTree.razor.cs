// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Front.Pages.Pipelines;

/// <summary>
/// Recursive, collapsible timeline for one pipeline run. Renders the run's own stages/steps; a
/// <c>type: trigger</c> step (<see cref="PipelineStepRunDto.TriggeredRunId"/>) becomes a collapsible child
/// node that lazy-loads and renders the triggered run's own timeline on expand, keyed by run id, so the
/// same pipeline triggered twice yields two distinct nodes. Selecting a real step bubbles up via
/// <see cref="OnSelectStep"/> so the parent page's shared log pane shows its logs.
/// </summary>
public partial class RunTimelineTree
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    // Keep child navigation as a native link: it remains usable even while Blazor is reconnecting.
    // stopPropagation in the markup prevents the containing expandable row from consuming the click.
    private string ChildRunHref(int childRunId) => PipelineRunPresentation.Href(childRunId, ProjectId, ServerId);

    [Parameter, EditorRequired] public PipelineRunDto Run { get; set; } = default!;
    [Parameter] public int Depth { get; set; }
    [Parameter] public int? SelectedStepId { get; set; }
    [Parameter] public EventCallback<PipelineStepRunDto> OnSelectStep { get; set; }
    [Parameter] public int? ProjectId { get; set; }
    [Parameter] public int? ServerId { get; set; }
    // Child runs already fetched by the parent page (keyed by run id): seeds this tree so expansion is
    // instant and the branch containing SelectedStepId can be auto-revealed without a per-click fetch.
    [Parameter] public IReadOnlyDictionary<int, PipelineRunDto>? PreloadedChildren { get; set; }
    [Parameter] public IReadOnlyDictionary<int, PipelineRunDto>? PreviousRuns { get; set; }

    // Belt-and-braces bound on recursion + the per-child fetch (the trigger cycle-guard already prevents loops).
    private const int MaxDepth = 6;

    private List<StageViewModel> _stages = [];
    private readonly HashSet<int> _expanded = [];
    private readonly Dictionary<int, PipelineRunDto?> _childRuns = [];
    private readonly HashSet<int> _loading = [];
    private bool _restored;

    // S-FEAT-EXST: which child-run nodes the user had open, persisted per run id so a SignalR reload or
    // a navigation away-and-back doesn't re-collapse the tree the user expanded during a live run.
    private string StorageKey => $"run-tl-exp-{Run.Id}";

    // Once the selected step's branch has been auto-expanded, don't fight the user re-collapsing it.
    private int? _autoExpandedFor;

    protected override void OnParametersSet()
    {
        _stages = RunStageBuilder.Build(Run);
        if (PreloadedChildren is not null)
            foreach (var kv in PreloadedChildren)
                _childRuns[kv.Key] = kv.Value;
        AutoExpandToSelected();
    }

    // Reveal the currently-selected step by expanding the trigger branch that contains it, so navigating
    // from the Overview to the Logs opens the timeline on the right node (not a collapsed child).
    private void AutoExpandToSelected()
    {
        if (SelectedStepId is not int sel || _autoExpandedFor == sel) return;
        _autoExpandedFor = sel;
        foreach (var stage in _stages)
            foreach (var step in stage.Steps)
                if (step.TriggeredRunId is int childId && _childRuns.TryGetValue(childId, out var child) && child is not null && ContainsStep(child, sel, []))
                    _expanded.Add(childId);
    }

    private bool ContainsStep(PipelineRunDto run, int stepId, HashSet<int> visitedRunIds)
    {
        if (!visitedRunIds.Add(run.Id)) return false;
        if (run.Steps.Any(s => s.Id == stepId)) return true;
        foreach (var s in run.Steps)
            if (s.TriggeredRunId is int cid && _childRuns.TryGetValue(cid, out var c) && c is not null && ContainsStep(c, stepId, visitedRunIds))
                return true;
        return false;
    }

    // Trigger step names follow the "Description (child-pipeline)" convention (e.g. "Build & test (toto-ci)").
    // Split them so the row renders the child pipeline as a chip and the description as the label, matching
    // the run-timeline mockup. Degrades gracefully to (whole name, null) when there is no trailing "(...)".
    private static (string Desc, string? Chip) SplitTriggerName(string stepName)
    {
        var open = stepName.LastIndexOf('(');
        if (open > 0 && stepName.EndsWith(')'))
        {
            var chip = stepName[(open + 1)..^1].Trim();
            var desc = stepName[..open].Trim();
            if (chip.Length > 0 && desc.Length > 0)
                return (desc, chip);
        }
        return (stepName, null);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _restored) return;
        _restored = true;

        int[]? saved = null;
        try
        {
            var json = await JS.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrEmpty(json)) saved = JsonSerializer.Deserialize<int[]>(json);
        }
        catch (Exception ex) when (ex is JSException or JsonException) { /* no/invalid persisted state */ }
        if (saved is null || saved.Length == 0) return;

        // Only re-open ids that are genuinely trigger children of THIS run (guards stale/foreign ids).
        var validChildIds = _stages.SelectMany(s => s.Steps)
            .Select(st => st.TriggeredRunId).Where(id => id is not null).Select(id => id!.Value).ToHashSet();
        foreach (var childId in saved)
            if (validChildIds.Contains(childId) && !_expanded.Contains(childId))
                await ToggleChild(childId);   // expand + lazy-fetch, exactly as a user click would

        await InvokeAsync(StateHasChanged);
    }

    private bool IsExpanded(int childRunId) => _expanded.Contains(childRunId);
    private bool IsLoading(int childRunId) => _loading.Contains(childRunId);
    private PipelineRunDto? ChildRun(int childRunId) => _childRuns.GetValueOrDefault(childRunId);

    private string? PreviousDuration(PipelineStepRunDto step)
    {
        if (step.Status != TaskExecutionStatus.Running || step.TriggeredRunId is not null
            || PreviousRuns?.GetValueOrDefault(Run.Id) is not { } previous)
            return null;

        var previousStep = previous.Steps.FirstOrDefault(candidate =>
            string.Equals(candidate.StageName, step.StageName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.StepName, step.StepName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizeMatrixLeg(candidate.MatrixLeg), NormalizeMatrixLeg(step.MatrixLeg),
                StringComparison.OrdinalIgnoreCase)
            && candidate.IsSystem == step.IsSystem);
        if (previousStep?.StartedAt is null || previousStep.CompletedAt is null) return null;
        return PipelineRunFormatting.FormatDuration(previousStep.StartedAt, previousStep.CompletedAt);
    }

    private static string NormalizeMatrixLeg(string? matrixLeg) => matrixLeg?.Trim() ?? string.Empty;

    private async Task ToggleChild(int childRunId)
    {
        // Add returns false when it was already present → this click collapses it.
        if (!_expanded.Add(childRunId)) { _expanded.Remove(childRunId); await PersistExpandedAsync(); return; }
        await PersistExpandedAsync();
        if (Depth >= MaxDepth) return;
        // Re-fetch on expand unless we already hold a *terminal* snapshot: a still-running child keeps
        // progressing after the first load, so its cached snapshot goes stale (and a prior failed fetch
        // cached null deserves a retry). Only a finished child is safe to keep.
        if (_childRuns.TryGetValue(childRunId, out var cached)
            && cached is not null && PipelineRunFormatting.IsTerminal(cached.Status)) return;

        _loading.Add(childRunId);
        // Null on any transport/shape failure → the "child run not found" note, never an unhandled crash
        // that would tear down the whole timeline (JsonException on a malformed body was previously uncaught).
        try { _childRuns[childRunId] = await Api.Pipelines.GetPipelineRunAsync(childRunId); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException) { _childRuns[childRunId] = null; }
        finally { _loading.Remove(childRunId); }
    }

    private async Task PersistExpandedAsync()
    {
        try { await JS.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(_expanded)); }
        catch (JSException) { /* storage unavailable (private mode / quota) - persistence is best-effort */ }
    }
}
