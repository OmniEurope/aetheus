// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared.Services;

public static class HubConnectionExtensions
{
    public static async Task LeaveAllServersAndDisposeAsync(this HubConnection connection)
    {
        try
        {
            await connection.InvokeAsync("LeaveAllServers").ConfigureAwait(false);
        }
        catch
        {
            // Leaving a group is best-effort when the transport is already unavailable.
        }

        await connection.DisposeAsync().ConfigureAwait(false);
    }

    public static async Task LeaveEntityUpdatesAndDisposeAsync(
        this HubConnection connection,
        ResourceType resourceType)
    {
        try
        {
            await connection.InvokeAsync("LeaveEntityUpdates", resourceType).ConfigureAwait(false);
        }
        catch
        {
            // Leaving a group is best-effort when the transport is already unavailable.
        }

        await connection.DisposeAsync().ConfigureAwait(false);
    }
}
