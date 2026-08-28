// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Services;

internal sealed class ServerBulkServiceActionCoordinator(ApiClient api, int serverId)
{
    internal async Task<bool> QueueAsync(string kind, string service, bool moduleInstallable)
    {
        if (kind == "Install" && moduleInstallable)
            return await api.Teamspeak.SetupTeamspeakAsync(serverId, new TeamspeakSetupRequest());
        var taskId = kind switch
        {
            "Install" => await api.Auth.InstallServiceAsync(serverId, service),
            "Uninstall" => await api.Auth.UninstallServiceAsync(serverId, service),
            "Start" => await QueueServiceActionAsync(ServiceAction.Start, service),
            "Stop" => await QueueServiceActionAsync(ServiceAction.Stop, service),
            "Restart" => await QueueServiceActionAsync(ServiceAction.Restart, service),
            _ => null
        };
        return taskId is not null;
    }

    private Task<int?> QueueServiceActionAsync(ServiceAction action, string service) =>
        api.Auth.ExecuteServiceActionAsync(serverId, new ServiceActionRequest
        {
            Action = action,
            ServiceName = service
        });
}
