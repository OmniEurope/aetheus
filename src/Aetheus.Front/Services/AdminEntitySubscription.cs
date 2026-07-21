// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.SignalR.Client;

namespace Aetheus.Front.Services;

/// <summary>
/// RT4M: reusable wrapper around the admin SignalR hub's <c>AdminEntityChanged</c> feed. Each admin
/// list page (Users, Roles, Organizations, Plugins) carried a near-identical block: create the
/// <c>admin</c> hub, subscribe to <c>AdminEntityChanged</c>, filter by its entity name, reload on a
/// match, and dispose the connection. This collapses those four copies into one collaborator: the page
/// supplies the entity name to watch and an <paramref name="onChanged"/> callback (which marshals to
/// the renderer and reloads its grid). Hub failures degrade silently to a static (non-live) view, as
/// before. Registered transient - each page owns its own instance and disposes it.
/// </summary>
public sealed class AdminEntitySubscription(HubConnectionFactory hubFactory) : IAsyncDisposable
{
    private HubConnection? _hub;

    /// <summary>Starts listening for changes to <paramref name="entityName"/> (an <c>AdminEntities</c>
    /// constant) and invokes <paramref name="onChanged"/> on each matching broadcast.</summary>
    public async Task StartAsync(string entityName, Func<Task> onChanged)
    {
        try
        {
            _hub = hubFactory.Create("admin");
            _hub.On<string, int, string>("AdminEntityChanged", (entity, _, _) =>
                entity == entityName ? onChanged() : Task.CompletedTask);
            using var timeout = new CancellationTokenSource(FrontendRuntimeDefaults.SignalRStartTimeout);
            await _hub.StartAsync(timeout.Token);
        }
        catch
        {
            // Realtime is optional for these list pages. A stalled negotiate must not block the
            // initial REST-backed render forever; retain the disconnected connection so the page's
            // normal disposal path still owns and releases the resource.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
            _hub = null;
        }
    }
}
