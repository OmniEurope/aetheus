// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Servers.Sections;

public partial class Docker
{
    private Aetheus.Front.Pages.Servers.ServerDetailSections.ServerDockerSection? _section;

    protected override Task HandleTaskCompletedNotificationAsync(
        Aetheus.Shared.DTOs.TaskCompletedNotification notification)
    {
        _section?.HandleTaskCompleted(notification);
        return Task.CompletedTask;
    }

    // ServerChanged on the section asks the parent to merge a fresh Server snapshot. The
    // loader is the single source of truth - heartbeats already refresh through Loader.OnChanged.
    private void OnServerChanged(Aetheus.Shared.DTOs.ServerDetailDto _) => StateHasChanged();

}
