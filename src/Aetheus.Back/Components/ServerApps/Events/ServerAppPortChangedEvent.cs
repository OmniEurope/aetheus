// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.ServerApps.Events;

/// <summary>
/// PLAN-005 lot 6: a server app's own port was set, moved or cleared.
///
/// It is an event rather than a direct call because <c>ServerApps</c> and <c>PortRegistry</c> are both
/// storage on the same layer and may not reach each other. The module that owns "a port belongs to
/// somebody" listens from above and records it; this one only states what happened to its own field.
///
/// A null <see cref="ServerId"/> or <see cref="Port"/> means the app claims no port any more, which is
/// what the deletion and the cleared-field cases both send.
/// </summary>
public sealed record ServerAppPortChangedEvent(
    int ServerAppId,
    int? ServerId,
    int? Port,
    string AppName) : IDomainEvent;
