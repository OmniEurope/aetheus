// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Hubs;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Components.AgentUpdate;

public sealed class AgentUpdateService(
    IServerRepository repo,
    IHubContext<ServerHub> serverHub,
    ITaskService taskService,
    IAuditService audit) : IAgentUpdateService
{
    // Self-update has to download the agent archive (a few MB), unpack it and hand off to the
    // platform updater. The agent reports the task complete as soon as the detached updater is
    // launched, so this generous ceiling only guards a stuck download.
    private const int UpdateTimeoutSeconds = 600;

    public async Task<AgentUpdateResponse> QueueUpdateAsync(int serverId, CancellationToken ct = default)
    {
        // FindServerAsync also covers the existence check and gives us the name in one query,
        // which is needed for the TaskQueued broadcast payload.
        var server = await repo.FindServerAsync(serverId, ct).ConfigureAwait(false);
        if (server is null)
            throw new NotFoundException($"Server {serverId} not found.");

        var task = BuildSelfUpdateTask(serverId);
        await repo.AddTaskAsync(task, ct).ConfigureAwait(false);
        await audit.LogAsync("AgentSelfUpdate", "Server", serverId, "Agent self-update queued", ct).ConfigureAwait(false);
        await BroadcastQueuedAsync(serverId, ct).ConfigureAwait(false);
        // Item #7: also surface to the top-bar widget through the canonical Tasks hook, so
        // the DTO shape / hub groups can never drift from TaskService's own broadcasts.
        await taskService.NotifyTaskQueuedAsync(task, server.Name, ct).ConfigureAwait(false);

        return new AgentUpdateResponse { TaskId = task.Id };
    }

    public async Task<AgentUpdateAllResponse> QueueUpdateAllAsync(List<int>? accessibleServerIds, CancellationToken ct = default)
    {
        // accessibleServerIds == null means "no scoping restriction" (e.g. unrestricted admin).
        // An empty list means the caller can administer nothing → nothing to queue.
        if (accessibleServerIds is { Count: 0 })
            return new AgentUpdateAllResponse();

        var servers = await repo.GetServerIdNamePairsAsync(accessibleServerIds, ct).ConfigureAwait(false);
        if (servers.Count == 0)
            return new AgentUpdateAllResponse();

        var tasks = servers.Select(s => BuildSelfUpdateTask(s.Id)).ToList();
        await repo.AddTasksAsync(tasks, ct).ConfigureAwait(false);

        var queuedIds = new List<int>(servers.Count);
        for (var i = 0; i < servers.Count; i++)
        {
            var (id, name) = servers[i];
            queuedIds.Add(id);
            await BroadcastQueuedAsync(id, ct).ConfigureAwait(false);
            await taskService.NotifyTaskQueuedAsync(tasks[i], name, ct).ConfigureAwait(false);
        }

        await audit.LogAsync("AgentSelfUpdateAll", "Server", null,
            $"Agent self-update queued for {queuedIds.Count} server(s)", ct).ConfigureAwait(false);

        return new AgentUpdateAllResponse { QueuedCount = queuedIds.Count, ServerIds = queuedIds };
    }

    private static ServerTask BuildSelfUpdateTask(int serverId) => new()
    {
        ServerId = serverId,
        Name = "Agent self-update",
        // Typed-operation pathway: no shell allow-list. Target is unused - the agent resolves
        // the download URL from its own configured ServerUrl.
        Command = string.Empty,
        Executor = ExecutorType.Operation,
        Operation = OperationKind.AgentSelfUpdate,
        Status = TaskExecutionStatus.Pending,
        TimeoutSeconds = UpdateTimeoutSeconds,
        EnvironmentVariables = "{}"
    };

    private async Task BroadcastQueuedAsync(int serverId, CancellationToken ct)
    {
        await Task.WhenAll(
            serverHub.Clients.Group(HubGroups.Server(serverId))
                .SendAsync("AgentUpdateQueued", serverId, ct),
            serverHub.Clients.Group(HubGroups.AllServers)
                .SendAsync("AgentUpdateQueued", serverId, ct)
        ).ConfigureAwait(false);
    }

    public async Task BroadcastProgressAsync(AgentUpdateProgressDto dto, CancellationToken ct = default)
    {
        // Defensive clamp: an agent reporting a corrupted percent must not poison the UI bar.
        // The progress is purely advisory - values outside [0,100] are coerced rather than
        // refused, so a buggy build still surfaces *something* useful (the phase enum).
        var clamped = dto with { Percent = Math.Clamp(dto.Percent, 0, 100) };
        await Task.WhenAll(
            serverHub.Clients.Group(HubGroups.Server(dto.ServerId))
                .SendAsync("AgentUpdateProgress", clamped, ct),
            serverHub.Clients.Group(HubGroups.AllServers)
                .SendAsync("AgentUpdateProgress", clamped, ct)
        ).ConfigureAwait(false);
    }
}
