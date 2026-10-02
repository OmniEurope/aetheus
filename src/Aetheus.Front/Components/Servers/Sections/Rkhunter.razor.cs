// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers.Sections;

public partial class Rkhunter
{
    private Aetheus.Front.Components.Servers.ServerDetailSections.ServerRkhunterSection? _section;

    protected override Task HandleTaskCompletedNotificationAsync(
        Aetheus.Shared.Components.Tasks.TaskCompletedNotification notification) =>
        _section?.HandleTaskCompletedAsync(notification) ?? Task.CompletedTask;
}
