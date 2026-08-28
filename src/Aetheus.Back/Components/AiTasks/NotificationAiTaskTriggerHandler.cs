// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Notifications.Events;
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.AiTasks;

/// <summary>
/// Runs whatever AI triggers are configured for a product event.
///
/// This is the AiTasks half of the inversion that removed the Notifications-to-AiTasks dependency.
/// Whether an event should start an AI task is this module's business; the notifier only states that
/// the event happened. The dispatch is the observer kind, so a failure here is logged and notification
/// delivery continues - the same guarantee the caught direct call gave.
/// </summary>
internal sealed class NotificationAiTaskTriggerHandler(IAiTaskService aiTasks)
    : IDomainEventHandler<NotificationEventRaisedEvent>
{
    public async Task HandleAsync(NotificationEventRaisedEvent domainEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        await aiTasks.DispatchEventAsync(domainEvent.EventType, domainEvent.Payload, ct).ConfigureAwait(false);
    }
}
