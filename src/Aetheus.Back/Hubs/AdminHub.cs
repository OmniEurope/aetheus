// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Hubs;

/// <summary>
/// Broadcasts CRUD-level changes for admin-managed entities that are NOT RBAC resources
/// (User, Role, Organization, Plugin) and therefore can't ride the <see cref="ResourceType"/>-keyed
/// <see cref="EntityHub"/>. Admin list pages open a connection here and receive
/// <c>AdminEntityChanged(entity, id, op)</c> on every Create/Update/Delete. The payload is a
/// non-sensitive notification (entity name + id + op); the data fetch the client then triggers
/// stays authorized by the API, so no per-connection group/scoping is needed - only pages that
/// open this hub receive its broadcasts.
/// </summary>
[Authorize(Roles = "Admin")]
public sealed class AdminHub : Hub;
