// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Hubs;

[Authorize]
public class ReleaseHub(IResourceAuthorizationService authz) : Hub
{
    public async Task JoinProjectGroup(int projectId)
    {
        if (!await authz.HasPermissionAsync(Context.User!, ResourceType.Project, projectId, Permission.Read).ConfigureAwait(false))
            throw new HubException("Access denied.");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"project-releases-{projectId}").ConfigureAwait(false);
    }

    public Task LeaveProjectGroup(int projectId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project-releases-{projectId}");

    public async Task JoinAllReleases()
    {
        // F-05: fan-out per accessible project. Admins keep the global aggregate group.
        var accessibleIds = await authz.GetAccessibleResourceIdsAsync(Context.User!, ResourceType.Project, Permission.Read).ConfigureAwait(false);
        if (accessibleIds is null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.AllReleases).ConfigureAwait(false);
            return;
        }
        if (accessibleIds.Count == 0)
            throw new HubException("Access denied.");
        foreach (var id in accessibleIds)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"project-releases-{id}").ConfigureAwait(false);
    }

    public async Task LeaveAllReleases()
    {
        var accessibleIds = await authz.GetAccessibleResourceIdsAsync(Context.User!, ResourceType.Project, Permission.Read).ConfigureAwait(false);
        if (accessibleIds is null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.AllReleases).ConfigureAwait(false);
            return;
        }
        foreach (var id in accessibleIds)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project-releases-{id}").ConfigureAwait(false);
    }
}
