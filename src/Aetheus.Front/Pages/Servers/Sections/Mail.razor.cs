// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Servers.Sections;

public partial class Mail
{
    private Aetheus.Front.Pages.Servers.ServerDetailSections.ServerMailSection? _section;

    // ServerMailSection.HandleTaskCompleted takes no argument - it just refreshes its state.
    protected override Task HandleTaskCompletedNotificationAsync(
        Aetheus.Shared.DTOs.TaskCompletedNotification _)
    {
        _section?.HandleTaskCompleted();
        return Task.CompletedTask;
    }
}
