// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.Tasks;

public interface ITaskRepository
{
    Task<(List<ServerTask> Items, int TotalCount)> GetTasksPagedAsync(
        string? search, int page, int pageSize, TaskExecutionStatus? status = null, CancellationToken ct = default);

    Task<(List<ServerTask> Items, int TotalCount)> GetTasksPagedAsync(
        string? search, int page, int pageSize, TaskExecutionStatus? status = null, List<int>? accessibleServerIds = null, int? serverId = null, CancellationToken ct = default);

    Task<(List<ServerTask> Items, int TotalCount)> GetTasksByStatusesPagedAsync(
        string? search, int page, int pageSize, List<TaskExecutionStatus> statuses, List<int>? accessibleServerIds = null, CancellationToken ct = default);

    Task<ServerTask?> GetTaskWithServerAsync(int id, CancellationToken ct = default);

    Task<ServerTask?> FindTaskAsync(int id, CancellationToken ct = default);
    Task<Dictionary<int, int>> GetServerIdsForTasksAsync(IReadOnlyCollection<int> taskIds, CancellationToken ct = default);

    /// <summary>Current agent status of a server, or null if the server does not exist.</summary>
    Task<ServerStatus?> GetServerStatusAsync(int serverId, CancellationToken ct = default);

    Task<List<ServerTask>> GetPendingTasksAsync(int serverId, CancellationToken ct = default);
    Task<List<ServerTask>> ClaimPendingTasksAsync(int serverId, int? take = null, CancellationToken ct = default);

    Task<ServerTask> AddTaskAsync(ServerTask task, CancellationToken ct = default);

    Task<PipelineStepRun?> FindPipelineStepRunAsync(int id, CancellationToken ct = default);
    Task<Dictionary<int, PipelineStepRun>> FindPipelineStepRunsByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);

    Task<List<ServerTask>> GetStaleRunningTasksAsync(TimeSpan threshold, CancellationToken ct = default);
    Task<List<ServerTask>> GetStaleAssignedTasksAsync(TimeSpan threshold, CancellationToken ct = default);
    Task<List<ServerTask>> GetStalePendingTasksAsync(TimeSpan threshold, CancellationToken ct = default);
    Task<int> DeleteCompletedTasksOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
    Task<Dictionary<int, string>> GetTaskStatusesAsync(List<int> taskIds, int serverId, CancellationToken ct = default);
}
