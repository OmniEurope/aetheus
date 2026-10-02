// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers.Sections;

public partial class Services
{
    private Aetheus.Front.Components.Servers.ServerDetailSections.ServerServicesSection? _section;

    protected override Task HandleTaskCompletedNotificationAsync(
        Aetheus.Shared.Components.Tasks.TaskCompletedNotification notification)
    {
        _section?.HandleTaskCompleted(notification);
        return Task.CompletedTask;
    }

    // Triggered by the section when a service install/uninstall/lifecycle action succeeds - force a
    // server-detail refresh so the installed/running lists update immediately (item F7).
    private async Task RefreshServerAsync()
    {
        if (Loader is not null) await Loader.RefreshAsync(Id);
    }

}
