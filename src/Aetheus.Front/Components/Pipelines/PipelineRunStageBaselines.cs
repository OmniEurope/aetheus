// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Front.Components.Pipelines;

/// <summary>
/// PLAN-003 lot 20 / D26. The usual durations of the stages and steps of the run on screen and of
/// its child runs, keyed by run id.
///
/// It replaces the previous-durations loader, which downloaded the whole run history of each
/// pipeline to keep one comparable run. The server now answers with an average over the last
/// successful runs; one small request per run, made once, since a run's history does not change
/// while it is watched.
/// </summary>
internal sealed class PipelineRunStageBaselines(ApiClient api)
{
    private readonly Dictionary<int, RunStageBaselinesDto> _byRunId = [];
    private readonly HashSet<int> _resolvedRunIds = [];

    public IReadOnlyDictionary<int, RunStageBaselinesDto> Runs => _byRunId;

    public async Task LoadAsync(IEnumerable<PipelineRunDto> runs)
    {
        var pending = runs
            .Where(run => !_resolvedRunIds.Contains(run.Id))
            .DistinctBy(run => run.Id)
            .ToList();
        if (pending.Count == 0) return;

        var results = await Task.WhenAll(pending.Select(LoadOneAsync));
        foreach (var (runId, baselines, succeeded) in results)
        {
            // A failed request is retried on the next reload rather than remembered as "no history".
            if (!succeeded) continue;
            _resolvedRunIds.Add(runId);
            if (baselines is { SampleRuns: > 0 })
                _byRunId[runId] = baselines;
        }
    }

    private async Task<(int RunId, RunStageBaselinesDto? Baselines, bool Succeeded)> LoadOneAsync(PipelineRunDto run)
    {
        try
        {
            return (run.Id, await api.Pipelines.GetStageBaselinesAsync(run.PipelineId, run.Id), true);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return (run.Id, null, false);
        }
    }
}
