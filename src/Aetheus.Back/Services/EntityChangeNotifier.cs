// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Hubs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Aetheus.Back.Services;

/// <summary>
/// Operation kind for <see cref="IEntityChangeNotifier"/>. Strings are used as the
/// payload contract for clients (case-sensitive).
/// </summary>
public static class EntityChangeOps
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";
}

public interface IEntityChangeNotifier
{
    /// <param name="organizationId">
    /// Owning organization id for org-scoped types (Project). When supplied, the event also targets the
    /// per-org aggregate group so a non-admin org member receives a Created event for a resource that did
    /// not exist when they joined (its per-resource group is empty at creation time). Omit for
    /// non-org-scoped types or when the org is unknown.
    /// </param>
    Task BroadcastAsync(ResourceType type, int id, string op, CancellationToken ct = default, int? organizationId = null);
}

/// <summary>
/// Pushes <c>EntityChanged(type, id, op)</c> messages over <see cref="EntityHub"/>.
/// Targets the aggregate group (admins / wildcards), the per-resource group (users with explicit
/// access) and, for org-scoped types, the per-org group (org members) so subscribers receive only
/// events they're allowed to see.
/// </summary>
public sealed class EntityChangeNotifier(IHubContext<EntityHub> hub, ILogger<EntityChangeNotifier> logger) : IEntityChangeNotifier
{
    // Types whose change events must also fan out to the per-org aggregate group. Keep in lockstep with
    // the HubGroups.EntityOrg routing: a caller that omits organizationId for one of these silently
    // strands org members who have not joined the per-resource group yet (the F-05 regression class).
    private static readonly HashSet<ResourceType> OrgScopedTypes = [ResourceType.Project, ResourceType.Pipeline];

    public Task BroadcastAsync(ResourceType type, int id, string op, CancellationToken ct = default, int? organizationId = null)
    {
        if (organizationId is null && OrgScopedTypes.Contains(type))
        {
            logger.LogWarning(
                "EntityChanged broadcast for org-scoped {Type} {Id} omitted organizationId; org members may miss this {Op} event",
                type, id, op);
        }

        var groups = organizationId is { } orgId
            ? new[] { HubGroups.EntityAll(type), HubGroups.Entity(type, id), HubGroups.EntityOrg(type, orgId) }
            : [HubGroups.EntityAll(type), HubGroups.Entity(type, id)];
        return hub.Clients.Groups(groups).SendAsync("EntityChanged", type, id, op, ct);
    }
}
