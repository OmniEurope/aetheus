// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Front.Pages.Pipelines;

/// <summary>
/// Resolves the latest comparable completed execution of each task in a currently running pipeline.
/// A single immediately-previous run is not sufficient: it may have been cancelled before reaching
/// the current task, while an older completed run still provides a useful duration reference.
/// </summary>
internal sealed class PipelineRunPreviousDurations(ApiClient api)
{
    private readonly Dictionary<int, PipelineRunDto> _previousByRunId = [];
    private readonly HashSet<int> _resolvedRunIds = [];

    public IReadOnlyDictionary<int, PipelineRunDto> Runs => _previousByRunId;

    public async Task LoadAsync(IEnumerable<PipelineRunDto> currentRuns)
    {
        var pending = currentRuns
            .Where(run => !_resolvedRunIds.Contains(run.Id)
                && run.Steps.Any(step => step.Status == TaskExecutionStatus.Running && step.TriggeredRunId is null))
            .DistinctBy(run => run.Id)
            .ToList();
        if (pending.Count == 0) return;

        var results = await Task.WhenAll(pending.Select(FindPreviousAsync));
        foreach (var result in results.Where(result => result.Succeeded))
        {
            _resolvedRunIds.Add(result.RunId);
            if (result.Previous is not null)
                _previousByRunId[result.RunId] = result.Previous;
        }
    }

    private async Task<(int RunId, PipelineRunDto? Previous, bool Succeeded)> FindPreviousAsync(PipelineRunDto current)
    {
        try
        {
            var runs = await api.Pipelines.GetPipelineRunsAsync(current.PipelineId);
            var history = runs
                .Where(run => run.Id != current.Id
                    && PipelineRunFormatting.IsTerminal(run.Status)
                    && (run.StartedAt < current.StartedAt
                        || run.StartedAt == current.StartedAt && run.Id < current.Id))
                .OrderByDescending(run => run.StartedAt)
                .ThenByDescending(run => run.Id)
                .ToList();
            if (history.Count == 0)
                return (current.Id, null, true);

            // Resolve every real task, including tasks that are still pending. The same snapshot can
            // then serve later live reloads when execution advances to the next task without another
            // history request.
            var comparableSteps = current.Steps
                .Where(step => step.TriggeredRunId is null)
                .Select(step => LatestComparableStep(history, step))
                .Where(step => step is not null)
                .Select(step => step!)
                .ToList();
            if (comparableSteps.Count == 0)
                return (current.Id, null, true);

            return (current.Id, history[0] with { Steps = comparableSteps }, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return (current.Id, null, false);
        }
    }

    private static PipelineStepRunDto? LatestComparableStep(
        IEnumerable<PipelineRunDto> history,
        PipelineStepRunDto currentStep) => history
        .SelectMany(run => run.Steps)
        .FirstOrDefault(candidate =>
            candidate.StartedAt is not null
            && candidate.CompletedAt is not null
            && string.Equals(candidate.StageName, currentStep.StageName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.StepName, currentStep.StepName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizeMatrixLeg(candidate.MatrixLeg), NormalizeMatrixLeg(currentStep.MatrixLeg),
                StringComparison.OrdinalIgnoreCase)
            && candidate.IsSystem == currentStep.IsSystem);

    private static string NormalizeMatrixLeg(string? matrixLeg) => matrixLeg?.Trim() ?? string.Empty;
}
