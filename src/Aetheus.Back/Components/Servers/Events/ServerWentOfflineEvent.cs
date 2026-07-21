// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Servers.Events;

/// <summary>
/// Raised when a server transitions to <c>Offline</c> after missing its heartbeat window.
/// Allows downstream handlers (audit, notifications, alert escalation) to react without
/// coupling them to <c>ServerTimeoutService</c>.
/// </summary>
public sealed record ServerWentOfflineEvent(int ServerId, string ServerName, DateTime? LastHeartbeat) : IDomainEvent;
