// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.PortRegistry;
using Aetheus.Back.Components.ServerApps.Events;
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.PortAllocation;

/// <summary>
/// PLAN-005 lot 6: records a server app's port in the registry.
///
/// This is the upper half of the inversion that keeps <c>ServerApps</c> and <c>PortRegistry</c>, both
/// storage on the same layer, from reaching each other. Whether a port belongs to somebody is this
/// side's business; the app only states that its own field changed.
///
/// Dispatched strictly by the emitter, so a refusal - the port belongs to another holder - reaches the
/// caller as the conflict it is, instead of leaving an app row claiming a port the registry says is
/// taken.
/// </summary>
internal sealed class ServerAppPortRegistryHandler(IPortRegistryService registry)
    : IDomainEventHandler<ServerAppPortChangedEvent>
{
    public Task HandleAsync(ServerAppPortChangedEvent domainEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return registry.SyncOwnedPortAsync(
            domainEvent.ServerId,
            domainEvent.Port,
            PortRegistryService.ServerAppOwnerKey(domainEvent.ServerAppId),
            domainEvent.AppName,
            projectId: null,
            ct);
    }
}
