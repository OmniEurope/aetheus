// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Logs;

public interface ILogService
{
    Task<List<TaskLogDto>> GetTaskLogsAsync(int taskId, int? maxLines = null, CancellationToken ct = default);
    Task<List<string>> GetTaskOutputVariableLinesAsync(int taskId, CancellationToken ct = default);
    Task<List<TaskLogDto>> GetTaskLogsUnmaskedAsync(int taskId, int? maxLines = null, CancellationToken ct = default);
    Task AppendLogAsync(AppendLogRequest request, CancellationToken ct = default);
    Task AppendLogsAsync(List<AppendLogRequest> requests, CancellationToken ct = default);
}
