// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Components.Servers.ServerDetailSections;

/// <summary>
/// PLAN-005: reads the full output of a completed mail task through the task-log API. The
/// <c>TaskCompleted</c> SignalR notification never carries the output, so the mail tabs fetch it once the
/// task they queued has finished.
/// </summary>
public static class MailTaskOutputLoader
{
    public static async Task<string> ReadAsync(ApiClient api, int taskId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        var logs = await api.Monitoring.GetTaskLogsAsync(taskId, ct);
        return string.Join('\n', logs.OrderBy(l => l.Id).Select(l => l.Message));
    }
}
