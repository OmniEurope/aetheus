// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Logs;

public interface ILogRepository
{
    Task<List<TaskLog>> GetTaskLogsAsync(int taskId, int? maxLines = null, CancellationToken ct = default);
    Task<List<string>> GetTaskOutputVariableLinesAsync(int taskId, CancellationToken ct = default);
    Task<int?> GetPipelineRunIdForTaskAsync(int taskId, CancellationToken ct = default);
    Task<TaskLog> AddLogAsync(TaskLog log, CancellationToken ct = default);
    Task AddLogsAsync(List<TaskLog> logs, CancellationToken ct = default);
    Task<int> DeleteLogsOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
}
