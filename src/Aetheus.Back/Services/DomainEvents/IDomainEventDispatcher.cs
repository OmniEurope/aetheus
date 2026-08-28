// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Services.DomainEvents;

public interface IDomainEventDispatcher
{
    /// <summary>
    /// Synchronous in-process dispatch: awaits all registered handlers in the caller's scope.
    /// Use for events whose side-effects must complete before the caller continues
    /// (e.g. a release row created in response to a successful pipeline run).
    /// </summary>
    Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : IDomainEvent;

    /// <summary>
    /// Synchronous in-process dispatch that does NOT swallow handler failures.
    ///
    /// <see cref="DispatchAsync{TEvent}"/> treats handlers as independent observers: one failing must
    /// not stop the others, so failures are logged and the caller continues. That is right for audit
    /// or notification, and wrong for an event that replaces a direct call - the caller used to see
    /// the exception, and hiding it would turn a broken state machine into a silent no-op.
    ///
    /// Use this only where the emitter genuinely owns the outcome. A handler failure propagates to
    /// the caller, exactly as the method call it replaced did; the remaining handlers do not run.
    /// </summary>
    Task DispatchStrictAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : IDomainEvent;

    /// <summary>
    /// Fire-and-forget: enqueues handler dispatch on <see cref="IBackgroundTaskQueue"/>
    /// so the emitting transaction returns immediately. Use for non-critical observers
    /// (audit, notifications, metrics) where eventual consistency is acceptable.
    /// </summary>
    void Publish<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent;
}
