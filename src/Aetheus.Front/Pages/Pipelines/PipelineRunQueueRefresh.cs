// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

internal static class PipelineRunQueueRefresh
{
    public static async Task<PipelineRunDto> RefreshAsync(
        ApiClient api,
        PipelineRunDto root,
        IDictionary<int, PipelineRunDto> children,
        CancellationToken ct)
    {
        foreach (var runId in children.Keys.Prepend(root.Id).ToArray())
        {
            PipelineRunQueueStateDto? state;
            try { state = await api.Pipelines.GetPipelineRunQueueAsync(runId, ct); }
            catch (HttpRequestException) { continue; }
            if (state is null) continue;

            if (root.Id == runId)
                root = Apply(root, state);
            else if (children.TryGetValue(runId, out var child))
                children[runId] = Apply(child, state);
        }
        return root;
    }

    internal static PipelineRunDto Apply(PipelineRunDto run, PipelineRunQueueStateDto state)
    {
        var positions = state.Steps.ToDictionary(step => step.StepId);
        return run with
        {
            Steps = run.Steps.Select(step =>
                step.Status != TaskExecutionStatus.Assigned
                    ? step
                    : positions.TryGetValue(step.Id, out var queue)
                        ? step with { QueuePosition = queue.Position, QueueDepth = queue.Depth }
                        : step with { QueuePosition = null, QueueDepth = null }).ToList()
        };
    }
}
