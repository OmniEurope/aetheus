// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers.Sections;

public partial class Teamspeak
{
    private Aetheus.Front.Components.Servers.ServerDetailSections.ServerTeamspeakSection? _section;

    protected override Task HandleTaskCompletedNotificationAsync(
        Aetheus.Shared.Components.Tasks.TaskCompletedNotification notification) =>
        _section?.HandleTaskCompletedAsync(notification) ?? Task.CompletedTask;
}
