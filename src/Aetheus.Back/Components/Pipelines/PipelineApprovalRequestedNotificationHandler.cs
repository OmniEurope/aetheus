// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines.Events;
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Notifies an approval gate. The event is already published through the background queue by the
/// dispatch planner, so the publisher runs directly in that scope.
/// </summary>
public sealed class PipelineApprovalRequestedNotificationHandler(PipelineRunNotificationPublisher publisher)
    : IDomainEventHandler<PipelineApprovalRequestedEvent>
{
    public Task HandleAsync(PipelineApprovalRequestedEvent domainEvent, CancellationToken ct = default) =>
        publisher.PublishApprovalRequestedAsync(domainEvent, ct);
}
