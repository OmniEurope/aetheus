// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Servers.Sections;

public partial class Apache
{
    private Aetheus.Front.Pages.Servers.ServerDetailSections.ServerApacheSection? _section;

    protected override Task HandleTaskCompletedNotificationAsync(
        Aetheus.Shared.DTOs.TaskCompletedNotification notification)
    {
        _section?.HandleTaskCompleted(notification);
        return Task.CompletedTask;
    }
}
