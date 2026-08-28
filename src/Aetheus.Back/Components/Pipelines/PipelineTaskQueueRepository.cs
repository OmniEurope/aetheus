// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

internal sealed class PipelineTaskQueueRepository(AppDbContext db)
{
    public async Task<Dictionary<int, TaskQueuePosition>> GetPositionsAsync(
        IReadOnlyCollection<int> taskIds,
        CancellationToken ct)
    {
        if (taskIds.Count == 0) return [];

        var activeStatuses = new[] { TaskExecutionStatus.Pending, TaskExecutionStatus.Assigned };
        var requestedIds = taskIds.Distinct().ToArray();
        var positions = await db.Tasks
            .AsNoTracking()
            .Where(task => requestedIds.Contains(task.Id) && activeStatuses.Contains(task.Status))
            .Select(task => new
            {
                task.Id,
                Position = db.Tasks.Count(other =>
                    other.ServerId == task.ServerId
                    && activeStatuses.Contains(other.Status)
                    && (other.CreatedAt < task.CreatedAt
                        || (other.CreatedAt == task.CreatedAt && other.Id <= task.Id))),
                Depth = db.Tasks.Count(other =>
                    other.ServerId == task.ServerId
                    && activeStatuses.Contains(other.Status))
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return positions.ToDictionary(
            task => task.Id,
            task => new TaskQueuePosition(task.Position, task.Depth));
    }

    public async Task<List<PipelineRunQueueReference>> GetRunReferencesAsync(
        int runId,
        CancellationToken ct)
        => await db.PipelineStepRuns
            .AsNoTracking()
            .Where(step => step.PipelineRunId == runId
                && step.Status == TaskExecutionStatus.Assigned
                && step.TaskId != null)
            .Select(step => new PipelineRunQueueReference(step.Id, step.TaskId!.Value))
            .ToListAsync(ct)
            .ConfigureAwait(false);
}
