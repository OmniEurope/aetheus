// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Front.Pages.Pipelines;

/// <summary>Resolves the previous completed run only for pipelines that currently execute a real task.</summary>
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
            var runs = await api.GetPipelineRunsAsync(current.PipelineId);
            var previous = runs
                .Where(run => run.Id != current.Id
                    && PipelineRunFormatting.IsTerminal(run.Status)
                    && (run.StartedAt < current.StartedAt
                        || run.StartedAt == current.StartedAt && run.Id < current.Id))
                .OrderByDescending(run => run.StartedAt)
                .ThenByDescending(run => run.Id)
                .FirstOrDefault();
            return (current.Id, previous, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return (current.Id, null, false);
        }
    }
}
