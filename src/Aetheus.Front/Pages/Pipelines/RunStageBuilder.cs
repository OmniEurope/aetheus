// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

/// <summary>
/// Groups a run's step runs into <see cref="StageViewModel"/>s (one per stage), in step order. Shared by
/// the run page's own timeline and the recursive <c>RunTimelineTree</c> so a nested child run is grouped
/// exactly like the top-level run.
/// </summary>
internal static class RunStageBuilder
{
    public static List<StageViewModel> Build(PipelineRunDto? run)
    {
        if (run is null) return [];
        return run.Steps
            .GroupBy(s => s.StageName)
            .Select(g =>
            {
                var steps = g.OrderBy(s => s.Id).ToList();
                var firstStart = steps.Where(s => s.StartedAt.HasValue).Select(s => s.StartedAt).Min();
                var lastEnd = steps.All(s => s.CompletedAt.HasValue) ? steps.Max(s => s.CompletedAt) : null;
                var isSystem = steps.All(s => s.IsSystem);
                var displayName = g.Key.StartsWith("System:", StringComparison.Ordinal)
                    ? g.Key.Replace("System:", "", StringComparison.Ordinal)
                    : g.Key;
                return new StageViewModel
                {
                    Name = g.Key,
                    DisplayName = displayName,
                    Steps = steps,
                    Status = PipelineRunFormatting.GetAggregateStatus(steps),
                    StartedAt = firstStart,
                    CompletedAt = lastEnd,
                    IsSystem = isSystem,
                    GroupName = steps.FirstOrDefault()?.GroupName
                };
            })
            .ToList();
    }
}
