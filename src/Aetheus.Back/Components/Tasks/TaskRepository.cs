// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Tasks;

public class TaskRepository(AppDbContext db, TimeProvider timeProvider) : ITaskRepository
{
    public Task<(List<ServerTask> Items, int TotalCount)> GetTasksPagedAsync(
        string? search, int page, int pageSize, TaskExecutionStatus? status = null, CancellationToken ct = default)
        => GetTasksPagedAsync(search, page, pageSize, status, null, null, ct);

    public async Task<(List<ServerTask> Items, int TotalCount)> GetTasksPagedAsync(
        string? search, int page, int pageSize, TaskExecutionStatus? status = null, List<int>? accessibleServerIds = null, int? serverId = null, CancellationToken ct = default)
    {
        var query = db.Tasks.AsNoTracking().AsQueryable();

        if (accessibleServerIds is not null)
            query = query.Where(t => accessibleServerIds.Contains(t.ServerId));

        if (serverId.HasValue)
            query = query.Where(t => t.ServerId == serverId.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.Name.Contains(search));

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var items = await query
            .Include(t => t.Server)
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task<(List<ServerTask> Items, int TotalCount)> GetTasksByStatusesPagedAsync(
        string? search, int page, int pageSize, List<TaskExecutionStatus> statuses, List<int>? accessibleServerIds = null, CancellationToken ct = default)
    {
        var query = db.Tasks.AsNoTracking().AsQueryable();

        if (accessibleServerIds is not null)
            query = query.Where(t => accessibleServerIds.Contains(t.ServerId));

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.Name.Contains(search));

        if (statuses.Count > 0)
            query = query.Where(t => statuses.Contains(t.Status));

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var items = await query
            .Include(t => t.Server)
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task<ServerTask?> GetTaskWithServerAsync(int id, CancellationToken ct = default)
    {
        return await db.Tasks.AsNoTracking().Include(t => t.Server)
            .FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);
    }

    public async Task<ServerTask?> FindTaskAsync(int id, CancellationToken ct = default)
    {
        return await db.Tasks.FindAsync([id], ct).ConfigureAwait(false);
    }

    public async Task<ServerStatus?> GetServerStatusAsync(int serverId, CancellationToken ct = default)
    {
        return await db.Servers.AsNoTracking()
            .Where(s => s.Id == serverId)
            .Select(s => (ServerStatus?)s.Status)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<Dictionary<int, int>> GetServerIdsForTasksAsync(IReadOnlyCollection<int> taskIds, CancellationToken ct = default)
    {
        if (taskIds.Count == 0) return [];
        return await db.Tasks.AsNoTracking()
            .Where(t => taskIds.Contains(t.Id))
            .Select(t => new { t.Id, t.ServerId })
            .ToDictionaryAsync(t => t.Id, t => t.ServerId, ct).ConfigureAwait(false);
    }

    public async Task<List<ServerTask>> GetPendingTasksAsync(int serverId, CancellationToken ct = default)
    {
        return await db.Tasks
            .Where(t => t.ServerId == serverId && t.Status == TaskExecutionStatus.Pending)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<ServerTask>> ClaimPendingTasksAsync(int serverId, int? take = null, CancellationToken ct = default)
    {
        // Claim Pending work only. An Assigned task may already be executing locally even if its
        // StartTask RPC has not reached the backend yet; redispatching it risks duplicate side effects.
        // TaskTimeoutService handles a claim that genuinely never starts.
        var candidates = await db.Tasks
            .Where(t => t.ServerId == serverId && t.Status == TaskExecutionStatus.Pending)
            .OrderBy(t => t.CreatedAt)
            .Select(t => new { t.Id, t.Operation })
            .ToListAsync(ct).ConfigureAwait(false);

        // A self-update is exclusive and takes priority over normal work. Claiming it alongside an
        // older unsupported task lets that task reach the agent first and can wedge the very poller
        // needed to install the compatible binary. One update per poll also prevents duplicate swaps.
        var selfUpdate = candidates.FirstOrDefault(c => c.Operation == OperationKind.AgentSelfUpdate);
        var claimable = selfUpdate is not null
            ? candidates.Where(c => c.Id == selfUpdate.Id)
            : take is { } t
                ? candidates.Take(Math.Clamp(t, 0, 50))
                : candidates;
        var candidateIds = claimable.Select(c => c.Id).Distinct().ToList();

        if (candidateIds.Count == 0)
            return [];

        if (db.Database.IsRelational())
        {
            var assignedAt = timeProvider.GetUtcNow().UtcDateTime;
            // Batch atomic compare-and-set: claim all candidates in a single round-trip.
            var updatedRows = await db.Tasks
                .Where(t => candidateIds.Contains(t.Id) && t.Status == TaskExecutionStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.Status, TaskExecutionStatus.Assigned)
                    .SetProperty(t => t.AssignedAt, assignedAt), ct)
                .ConfigureAwait(false);

            if (updatedRows == 0)
                return [];
        }
        else
        {
            // InMemory (unit tests) does not support ExecuteUpdate - load + set instead.
            var tasks = await db.Tasks
                .Where(t => candidateIds.Contains(t.Id) && t.Status == TaskExecutionStatus.Pending)
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var task in tasks)
            {
                task.Status = TaskExecutionStatus.Assigned;
                task.AssignedAt = timeProvider.GetUtcNow().UtcDateTime;
            }
            if (tasks.Count > 0)
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (tasks.Count == 0)
                return [];
        }

        return await db.Tasks
            .Where(t => candidateIds.Contains(t.Id) && t.Status == TaskExecutionStatus.Assigned)
            .Include(t => t.PipelineStepRun)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<ServerTask> AddTaskAsync(ServerTask task, CancellationToken ct = default)
    {
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await db.Tasks.Include(t => t.Server)
            .FirstAsync(t => t.Id == task.Id, ct).ConfigureAwait(false);
    }

    public async Task<PipelineStepRun?> FindPipelineStepRunAsync(int id, CancellationToken ct = default)
    {
        return await db.PipelineStepRuns.FindAsync([id], ct).ConfigureAwait(false);
    }

    public async Task<Dictionary<int, PipelineStepRun>> FindPipelineStepRunsByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];
        return await db.PipelineStepRuns
            .Where(r => ids.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct).ConfigureAwait(false);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<ServerTask>> GetStaleRunningTasksAsync(TimeSpan threshold, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var cutoff = now - threshold;
        var candidates = await db.Tasks
            .Where(t => t.Status == TaskExecutionStatus.Running && t.StartedAt < cutoff)
            .ToListAsync(ct).ConfigureAwait(false);

        // The configured threshold is the safety-net minimum, not a ceiling for legitimate long tasks.
        // Pipeline steps carry their own timeout and the agent is allowed to run for that duration. Filter
        // the small set of globally-old Running tasks in memory so provider-specific date arithmetic is not
        // required and the watchdog cannot pre-empt a task whose declared timeout is longer than the global
        // fallback.
        return candidates
            .Where(task => task.StartedAt.HasValue
                && now - task.StartedAt.Value > Max(threshold, TimeSpan.FromSeconds(Math.Max(0, task.TimeoutSeconds))))
            .ToList();

        static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;
    }

    public async Task<List<ServerTask>> GetStaleAssignedTasksAsync(TimeSpan threshold, CancellationToken ct = default)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime - threshold;
        return await db.Tasks
            .Where(t => t.Status == TaskExecutionStatus.Assigned && t.StartedAt == null
                && (t.AssignedAt ?? t.CreatedAt) < cutoff)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<ServerTask>> GetStalePendingTasksAsync(TimeSpan threshold, CancellationToken ct = default)
    {
        // Pending tasks an agent never claimed (offline/crashed before polling) - they never reach
        // Assigned/Running, so the other sweeps miss them and they stay in-flight forever.
        var cutoff = timeProvider.GetUtcNow().UtcDateTime - threshold;
        return await db.Tasks
            .Where(t => t.Status == TaskExecutionStatus.Pending && t.CreatedAt < cutoff)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> DeleteCompletedTasksOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        // Terminal tasks that are NOT linked to a still-present run (PipelineRunId == null): either
        // standalone ops (service/cron/package) or pipeline tasks whose run was already purged
        // (the run FK is SetNull on delete, so a purged run orphans its tasks). A pipeline task
        // still pointing at a live run is KEPT - the run-detail view sources each step's Command
        // from step.Task.Command and its logs cascade from the task, so purging by task-age alone
        // would blank the Command + logs of a run still inside its own retention window.
        // Set-based on relational (the table grows forever otherwise); InMemory fallback for tests.
        var terminalStatuses = new[]
        {
            TaskExecutionStatus.Success, TaskExecutionStatus.Failed,
            TaskExecutionStatus.Cancelled, TaskExecutionStatus.Timeout
        };

        if (db.Database.IsRelational())
        {
            return await db.Tasks
                .Where(t => t.PipelineRunId == null && terminalStatuses.Contains(t.Status) && t.CompletedAt != null && t.CompletedAt < cutoff)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }

        var old = await db.Tasks
            .Where(t => t.PipelineRunId == null && terminalStatuses.Contains(t.Status) && t.CompletedAt != null && t.CompletedAt < cutoff)
            .ToListAsync(ct).ConfigureAwait(false);
        db.Tasks.RemoveRange(old);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return old.Count;
    }

    public async Task<Dictionary<int, string>> GetTaskStatusesAsync(List<int> taskIds, int serverId, CancellationToken ct = default)
    {
        return await db.Tasks.AsNoTracking()
            .Where(t => taskIds.Contains(t.Id) && t.ServerId == serverId)
            .Select(t => new { t.Id, t.Status })
            .ToDictionaryAsync(t => t.Id, t => t.Status.ToString(), ct).ConfigureAwait(false);
    }
}
