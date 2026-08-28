// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

/// <summary>Resolves trigger-step run ancestry without loading complete run aggregates.</summary>
internal sealed class PipelineRunLineageRepository(AppDbContext db) : IPipelineRunLineageReader
{
    public async Task<Dictionary<int, PipelineRunRootReference>> GetRootRunReferencesAsync(
        IReadOnlyCollection<int> runIds, CancellationToken ct = default)
    {
        var requestedRunIds = runIds.Where(id => id > 0).Distinct().ToList();
        if (requestedRunIds.Count == 0)
            return [];

        var rootByRequested = requestedRunIds.ToDictionary(id => id, id => id);
        var visitedByRequested = requestedRunIds.ToDictionary(id => id, id => new HashSet<int> { id });

        while (true)
        {
            var currentRunIds = rootByRequested.Values.Distinct().ToList();
            var parentLinks = await db.PipelineStepRuns.AsNoTracking()
                .Where(step => step.TriggeredRunId.HasValue
                    && currentRunIds.Contains(step.TriggeredRunId.Value))
                .OrderBy(step => step.Id)
                .Select(step => new
                {
                    ChildRunId = step.TriggeredRunId!.Value,
                    ParentRunId = step.PipelineRunId
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var parentByChild = parentLinks
                .GroupBy(link => link.ChildRunId)
                .ToDictionary(group => group.Key, group => group.First().ParentRunId);
            var moved = false;

            foreach (var requestedRunId in requestedRunIds)
            {
                var currentRunId = rootByRequested[requestedRunId];
                if (!parentByChild.TryGetValue(currentRunId, out var parentRunId)
                    || !visitedByRequested[requestedRunId].Add(parentRunId))
                    continue;

                rootByRequested[requestedRunId] = parentRunId;
                moved = true;
            }

            if (!moved)
                break;
        }

        var rootRunIds = rootByRequested.Values.Distinct().ToList();
        var roots = await db.PipelineRuns.AsNoTracking()
            .Where(run => rootRunIds.Contains(run.Id))
            .Select(run => new PipelineRunRootReference(run.Id, run.PipelineId, run.Pipeline.Name))
            .ToDictionaryAsync(root => root.RunId, ct)
            .ConfigureAwait(false);

        return rootByRequested
            .Where(pair => roots.ContainsKey(pair.Value))
            .ToDictionary(pair => pair.Key, pair => roots[pair.Value]);
    }
}
