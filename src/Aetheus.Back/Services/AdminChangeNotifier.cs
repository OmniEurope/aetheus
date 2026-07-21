// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Services;

// Admin entity-name constants moved to Aetheus.Shared.Constants.AdminEntities (S-TECH-RT4M) so the
// backend broadcast and the frontend subscription filter share one case-sensitive contract. A global
// using alias keeps the unqualified `AdminEntities.X` references across the backend working.

public interface IAdminChangeNotifier
{
    Task BroadcastAsync(string entity, int id, string op, CancellationToken ct = default);
}

/// <summary>
/// Pushes <c>AdminEntityChanged(entity, id, op)</c> over <see cref="AdminHub"/> to every connected
/// client (the admin list pages that opened the hub). Reuses <see cref="EntityChangeOps"/> for the op.
/// </summary>
public sealed class AdminChangeNotifier(IHubContext<AdminHub> hub) : IAdminChangeNotifier
{
    public Task BroadcastAsync(string entity, int id, string op, CancellationToken ct = default)
        => hub.Clients.All.SendAsync("AdminEntityChanged", entity, id, op, ct);
}
