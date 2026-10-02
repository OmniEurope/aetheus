// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// A list that stays current without a Refresh button: it follows the <c>EntityChanged</c> pushes of the
/// scoped <c>entities</c> hub for the resource types it shows, and the changes of the caller's
/// permissions. The hub only delivers a change to connections allowed to read that resource; the list
/// then fetches through the authorized API. Group membership is per connection and lost on a
/// reconnect, so the groups are joined again and the list refreshed then. An unavailable hub leaves
/// the list on what it loaded, without throwing.
/// </summary>
public abstract class EntityLiveListBase : ComponentBase, IAsyncDisposable
{
    [Inject] protected ApiClient Api { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;
    [Inject] protected IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] protected PermissionService Permissions { get; set; } = default!;
    [Inject] protected HubConnectionFactory HubFactory { get; set; } = default!;

    private HubConnection? _entities;
    private ResourceType[] _followed = [];
    private bool _followsPermissions;

    /// <summary>Runs on the renderer when an entity of a followed type changed, and after a reconnect.</summary>
    protected abstract Task OnEntitiesChangedAsync();

    /// <summary>Runs when the caller's permissions changed, once <see cref="FollowPermissions"/> was called.</summary>
    protected abstract void OnPermissionsChanged();

    protected void FollowPermissions()
    {
        if (_followsPermissions) return;
        _followsPermissions = true;
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
    }

    /// <summary>Opens the hub connection and joins the update group of each of <paramref name="types"/>.</summary>
    protected async Task FollowEntitiesAsync(params ResourceType[] types)
    {
        _followed = types;
        try
        {
            var hub = HubFactory.Create("entities");
            _entities = hub;
            hub.On<ResourceType, int, string>("EntityChanged", (type, _, _) => OnEntityChangedAsync(type));
            hub.RejoinOnReconnect(() => InvokeAsync(async () =>
            {
                await JoinAsync(hub, types);
                await OnEntitiesChangedAsync();
            }));
            await hub.StartAsync();
            await JoinAsync(hub, types);
        }
        catch
        {
            // Hub unavailable: the list works without live updates.
        }
    }

    /// <summary>An <c>EntityChanged</c> push: only a followed type refreshes the list.</summary>
    internal Task OnEntityChangedAsync(ResourceType type)
        => Array.IndexOf(_followed, type) >= 0 ? InvokeAsync(OnEntitiesChangedAsync) : Task.CompletedTask;

    private static async Task JoinAsync(HubConnection hub, ResourceType[] types)
    {
        foreach (var type in types)
            await hub.InvokeAsync("JoinEntityUpdates", type);
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_followsPermissions)
            Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        if (_entities is { } hub)
        {
            _entities = null;
            foreach (var type in _followed)
            {
                try { await hub.InvokeAsync("LeaveEntityUpdates", type); }
                catch { /* Leaving a group is best-effort when the transport is already unavailable. */ }
            }

            await hub.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
