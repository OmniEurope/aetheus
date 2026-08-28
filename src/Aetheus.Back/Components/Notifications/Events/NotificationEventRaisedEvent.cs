// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Notifications.Events;

/// <summary>
/// A product event was raised for notification delivery, and anything else that reacts to events may
/// now act on it.
///
/// Notifications used to call the AI module directly through a trigger port, which put the two in a
/// cycle for what is a purely reactive concern: whether an AI task runs on an event is the AI
/// module's business, not the notifier's. Raising the event inverts that - Notifications states what
/// happened, subscribers decide what it means.
///
/// Dispatched non-strictly, deliberately. The call it replaces was wrapped in a catch that logged and
/// let notification delivery continue: an AI trigger must not be able to stop a notification from
/// being sent. That is exactly what the observer dispatch does.
/// </summary>
/// <param name="EventType">Product event name the notification rules are keyed on.</param>
/// <param name="Payload">Event payload, serialized by whichever subscriber needs it.</param>
public sealed record NotificationEventRaisedEvent(string EventType, object Payload) : IDomainEvent;
