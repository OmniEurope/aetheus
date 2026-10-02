// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Components.Tasks;

public sealed class TaskQueueNotifier(
    IHubContext<ServerHub> serverHub,
    ILogger<TaskQueueNotifier> logger) : ITaskQueueNotifier
{
    public async Task NotifyTaskQueuedAsync(
        ServerTask task,
        string? serverNameOverride = null,
        CancellationToken ct = default)
    {
        var dto = TaskDtoMapper.ToMaskedDto(task);
        if (!string.IsNullOrEmpty(serverNameOverride) && string.IsNullOrEmpty(dto.ServerName))
            dto = dto with { ServerName = serverNameOverride };

        try
        {
            await Task.WhenAll(
                serverHub.Clients.Group(HubGroups.Server(dto.ServerId))
                    .SendAsync("TaskQueued", dto, ct),
                serverHub.Clients.Group(HubGroups.AllServers)
                    .SendAsync("TaskQueued", dto, ct)
            ).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "TaskQueued broadcast failed for task {TaskId} on server {ServerId}; the task is persisted and will reconcile on the next widget refresh.",
                dto.Id,
                dto.ServerId);
        }
    }
}
