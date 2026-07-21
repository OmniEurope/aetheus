// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Constants;

/// <summary>
/// Reason codes pushed with the per-user <c>PermissionsChanged(reason)</c> realtime event over
/// <c>/hubs/user</c>. Shared between backend (broadcast) and frontend (display/telemetry) so the
/// string contract can never drift. The payload is deliberately tiny; the client re-fetches the
/// authoritative permission set from the API when it receives the event.
/// </summary>
public static class PermissionChangeReasons
{
    /// <summary>The user's role assignments changed (roles added/removed, or the user deactivated).</summary>
    public const string Roles = "Roles";

    /// <summary>A role the user holds had its resource permissions changed.</summary>
    public const string RolePermissions = "RolePermissions";

    /// <summary>The user's organization membership changed (added/removed, role changed, org deleted).</summary>
    public const string OrganizationMembership = "OrganizationMembership";
}
