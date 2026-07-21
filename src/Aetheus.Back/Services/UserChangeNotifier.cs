// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Services;

public interface IUserChangeNotifier
{
    /// <summary>
    /// Pushes <c>PermissionsChanged(reason)</c> to every connection of the given user over
    /// <see cref="UserHub"/>. Called after the change is persisted and the authz cache invalidated.
    /// </summary>
    Task NotifyPermissionsChangedAsync(int userId, string reason, CancellationToken ct = default);
}

/// <summary>
/// Targets a single user via <c>Clients.User(userId)</c>. The default SignalR IUserIdProvider maps
/// to the JWT <c>NameIdentifier</c> claim, which equals the user id (AuthService.BuildUserClaims),
/// so no extra wiring is required.
/// </summary>
public sealed class UserChangeNotifier(IHubContext<UserHub> hub) : IUserChangeNotifier
{
    public Task NotifyPermissionsChangedAsync(int userId, string reason, CancellationToken ct = default)
        => hub.Clients.User(userId.ToString()).SendAsync("PermissionsChanged", reason, ct);
}
