// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// RT4M: reusable wrapper around the admin SignalR hub's <c>AdminEntityChanged</c> feed. Each admin
/// list page (Users, Roles, Organizations, Plugins) carried a near-identical block: create the
/// <c>admin</c> hub, subscribe to <c>AdminEntityChanged</c>, filter by its entity name, reload on a
/// match, and dispose the connection. This collapses those copies into one collaborator: the page
/// supplies the entity name(s) to watch and an <c>onChanged</c> callback (which marshals to the renderer
/// and reloads its grid). After a reconnect the callback runs once, so a change missed while the hub
/// was down still shows. There is no polling path (recette R-181: realtime by push only). Each page
/// owns its own instance and disposes it.
/// </summary>
public sealed class AdminEntitySubscription(HubConnectionFactory hubFactory) : IAsyncDisposable
{
    private HubConnection? _hub;

    /// <summary>Starts listening for changes to <paramref name="entityName"/> (an <c>AdminEntities</c>
    /// constant) and invokes <paramref name="onChanged"/> on each matching broadcast.</summary>
    public Task StartAsync(string entityName, Func<Task> onChanged)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        return StartAsync([entityName], onChanged);
    }

    /// <summary>Same, for a view that depends on several entities (a user's effective permissions
    /// follow users, roles and organizations): one hub connection, any listed name triggers.</summary>
    public async Task StartAsync(IReadOnlyCollection<string> entityNames, Func<Task> onChanged)
    {
        ArgumentNullException.ThrowIfNull(entityNames);
        if (entityNames.Count == 0 || entityNames.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one entity name, none blank.", nameof(entityNames));
        ArgumentNullException.ThrowIfNull(onChanged);
        var watched = new HashSet<string>(entityNames, StringComparer.Ordinal);
        try
        {
            _hub = hubFactory.Create("admin");
            _hub.On<string, int, string>("AdminEntityChanged", (entity, _, _) =>
                watched.Contains(entity) ? onChanged() : Task.CompletedTask);
            _hub.Reconnected += async _ =>
            {
                try { await onChanged().ConfigureAwait(false); }
                catch { /* reconnect reconciliation is best-effort */ }
            };
            using var timeout = new CancellationTokenSource(FrontendRuntimeDefaults.SignalRStartTimeout);
            await _hub.StartAsync(timeout.Token);
        }
        catch
        {
            // Hub unreachable at start: the page keeps what it loaded, without live updates until it
            // is opened again (best effort, as before; the removed polling fallback had no caller).
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync().ConfigureAwait(false);
            _hub = null;
        }
    }
}
