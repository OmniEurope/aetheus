// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Releases;

/// <summary>
/// Applies the release-state transition after the deploy task confirms the rollback target is healthy.
/// </summary>
public sealed class RollbackDeploymentSucceededHandler(IReleaseService releaseService)
    : IDomainEventHandler<RollbackDeploymentSucceededEvent>
{
    public Task HandleAsync(RollbackDeploymentSucceededEvent domainEvent, CancellationToken ct = default)
        => releaseService.NotifyRollbackDeploymentSucceededAsync(domainEvent.RollbackId, ct);
}
