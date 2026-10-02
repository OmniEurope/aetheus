// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines.Events;
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Records a finished run as user deliveries (see <see cref="PipelineRunNotificationPublisher"/>: user
/// deliveries only, no admin channel, no AI trigger). The completion event is dispatched synchronously
/// inside the finalizer, so the work (database reads, a delivery write) is moved onto the background
/// queue in its own scope: it can neither slow the finalizer down nor flush the finalizer's tracked
/// changes through a shared DbContext.
/// </summary>
public sealed class PipelineRunCompletedNotificationHandler(IBackgroundTaskQueue queue)
    : IDomainEventHandler<PipelineRunCompletedEvent>
{
    public Task HandleAsync(PipelineRunCompletedEvent domainEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (PipelineRunNotificationPublisher.EventTypeFor(domainEvent.Status) is null)
            return Task.CompletedTask;

        queue.Enqueue((services, token) => services
            .GetRequiredService<PipelineRunNotificationPublisher>()
            .PublishRunCompletedAsync(domainEvent.PipelineRunId, domainEvent.Status, token));
        return Task.CompletedTask;
    }
}
