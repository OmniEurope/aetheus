// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;

namespace Aetheus.Front.Pages.Pipelines;

/// <summary>Loads an orchestration run's triggered child runs and bubbles their result data
/// (coverage / lint / code-quality metrics / test results) up onto the parent run, so an orchestration
/// surfaces the same tabs and overview tiles as the child pipelines that actually produced them.
/// Extracted from <see cref="PipelineRun"/> to keep that component under the file-size budget.</summary>
internal static class PipelineRunChildAggregator
{
    /// <summary>Fetches the distinct triggered child runs (keyed by id) and returns them alongside a copy of
    /// <paramref name="run"/> with the children's summaries merged in (the parent's own values win when set).
    /// On a transient fetch failure the original run is returned with whatever children were cached.</summary>
    public static async Task<(Dictionary<int, PipelineRunDto> Children, PipelineRunDto Run)> LoadAsync(
        PipelineRunDto run, ApiClient api, CancellationToken ct = default)
    {
        var cache = new Dictionary<int, PipelineRunDto>();
        var childIds = run.Steps.Where(s => s.TriggeredRunId is not null)
            .Select(s => s.TriggeredRunId!.Value).Distinct().ToList();
        if (childIds.Count == 0) return (cache, run);

        var fetched = new PipelineRunDto?[childIds.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, childIds.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (index, cancellationToken) =>
        {
            try { fetched[index] = await api.GetPipelineRunAsync(childIds[index], cancellationToken); }
            catch (HttpRequestException) { /* preserve successfully fetched children */ }
        });
        foreach (var child in fetched)
            if (child is not null) cache[child.Id] = child;

        var children = cache.Values.ToList();
        if (children.Count == 0) return (cache, run);

        var merged = run with
        {
            CoverageSummary = run.CoverageSummary ?? children.Select(c => c.CoverageSummary).FirstOrDefault(x => x is not null),
            LintSummary = run.LintSummary ?? children.Select(c => c.LintSummary).FirstOrDefault(x => x is not null),
            TestResultSummary = run.TestResultSummary ?? AggregateTestResults(children),
            Metrics = run.Metrics.Count > 0 ? run.Metrics : children.SelectMany(c => c.Metrics).ToList(),
        };
        return (cache, merged);
    }

    private static PipelineTestResultSummaryDto? AggregateTestResults(List<PipelineRunDto> children)
    {
        var summaries = children.Select(c => c.TestResultSummary).OfType<PipelineTestResultSummaryDto>().ToList();
        if (summaries.Count == 0) return null;
        return new PipelineTestResultSummaryDto
        {
            TotalTests = summaries.Sum(s => s.TotalTests),
            Passed = summaries.Sum(s => s.Passed),
            Failed = summaries.Sum(s => s.Failed),
            Skipped = summaries.Sum(s => s.Skipped),
            Errors = summaries.Sum(s => s.Errors),
            TotalDurationMs = summaries.Sum(s => s.TotalDurationMs),
        };
    }
}
