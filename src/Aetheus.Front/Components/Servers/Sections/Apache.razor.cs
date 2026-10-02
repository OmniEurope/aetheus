// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers.Sections;

public partial class Apache
{
    private Aetheus.Front.Components.Servers.ServerDetailSections.ServerApacheSection? _section;

    protected override Task HandleTaskCompletedNotificationAsync(
        Aetheus.Shared.Components.Tasks.TaskCompletedNotification notification)
    {
        _section?.HandleTaskCompleted(notification);
        return Task.CompletedTask;
    }
}
