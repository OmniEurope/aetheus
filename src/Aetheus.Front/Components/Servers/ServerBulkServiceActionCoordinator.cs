// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers;

internal sealed class ServerBulkServiceActionCoordinator(ApiClient api, int serverId)
{
    /// <summary>Queues one action of a batch. <c>TaskId</c> is the task to follow on the service's row;
    /// the module installer (TeamSpeak) answers without one.</summary>
    internal async Task<(bool Queued, int? TaskId)> QueueAsync(string kind, string service, bool moduleInstallable)
    {
        if (kind == "Install" && moduleInstallable)
            return (await api.Teamspeak.SetupTeamspeakAsync(serverId, new TeamspeakSetupRequest()), null);
        var taskId = kind switch
        {
            "Install" => await api.Auth.InstallServiceAsync(serverId, service),
            "Uninstall" => await api.Auth.UninstallServiceAsync(serverId, service),
            "Start" => await QueueServiceActionAsync(ServiceAction.Start, service),
            "Stop" => await QueueServiceActionAsync(ServiceAction.Stop, service),
            "Restart" => await QueueServiceActionAsync(ServiceAction.Restart, service),
            _ => null
        };
        return (taskId is not null, taskId);
    }

    private Task<int?> QueueServiceActionAsync(ServiceAction action, string service) =>
        api.Auth.ExecuteServiceActionAsync(serverId, new ServiceActionRequest
        {
            Action = action,
            ServiceName = service
        });
}
