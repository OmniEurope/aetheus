// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Servers.Sections;

public partial class Updates
{
    private Aetheus.Front.Pages.Servers.ServerDetailSections.ServerPatchSection? _section;

    protected override Task HandleTaskCompletedNotificationAsync(
        Aetheus.Shared.DTOs.TaskCompletedNotification notification) =>
        _section?.HandleTaskCompletedAsync(notification) ?? Task.CompletedTask;
}
