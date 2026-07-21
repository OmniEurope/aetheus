// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Hubs;

[Authorize(Roles = "Admin")]
public class AlertHub : Hub
{
    public Task JoinAlertGroup()
        => Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Alerts);

    public Task LeaveAlertGroup()
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Alerts);
}
