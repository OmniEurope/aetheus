// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Services.DomainEvents;

public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct = default);
}
