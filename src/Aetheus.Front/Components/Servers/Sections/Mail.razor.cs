// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers.Sections;

public partial class Mail
{
    private Aetheus.Front.Components.Servers.ServerDetailSections.ServerMailSection? _section;

    // PLAN-005: the mail section forwards the notification to the tabs waiting for a task output.
    protected override Task HandleTaskCompletedNotificationAsync(
        Aetheus.Shared.Components.Tasks.TaskCompletedNotification notification)
    {
        _section?.HandleTaskCompleted(notification);
        return Task.CompletedTask;
    }
}
