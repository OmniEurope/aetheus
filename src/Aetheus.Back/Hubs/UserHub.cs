// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Hubs;

/// <summary>
/// Per-user notification channel. A connected user receives <c>PermissionsChanged(reason)</c> the
/// instant an admin changes their roles, an org membership, or the permissions of a role they hold,
/// so the client can force a permission refresh (see the frontend UserNotificationService).
/// Messages are addressed with <c>Clients.User(userId)</c>; SignalR's default IUserIdProvider keys
/// off the JWT <c>NameIdentifier</c> claim, which AuthService already sets to the user id, so no
/// custom provider is needed. The payload is a non-sensitive reason code; the authoritative data is
/// re-fetched via the API.
/// </summary>
[Authorize]
public sealed class UserHub : Hub;
