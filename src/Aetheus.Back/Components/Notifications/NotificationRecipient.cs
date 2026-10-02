// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Notifications;

/// <summary>A user an event may be notified to, before the read permission on its project is checked.</summary>
public sealed record NotificationRecipient(int UserId, string Username);
