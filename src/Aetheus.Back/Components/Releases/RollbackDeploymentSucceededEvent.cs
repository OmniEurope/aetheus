// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Releases;

/// <summary>
/// Raised after a rollback deployment task has passed its real deploy health gate.
/// </summary>
public sealed record RollbackDeploymentSucceededEvent(int RollbackId) : IDomainEvent;
