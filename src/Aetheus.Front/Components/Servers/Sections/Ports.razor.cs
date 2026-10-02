// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers.Sections;

public partial class Ports
{
    private Aetheus.Front.Components.Servers.ServerDetailSections.ServerPortsSection? _section;

    // A finished agent scan is a completed ServerTask, so the section reloads on that signal instead of
    // leaving the operator to refresh the page to see what the scan found.
    protected override Task HandleTaskCompletedNotificationAsync(
        Aetheus.Shared.Components.Tasks.TaskCompletedNotification notification) =>
        _section?.HandleTaskCompletedAsync(notification) ?? Task.CompletedTask;
}
