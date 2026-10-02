// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Servers.Events;
using Aetheus.Back.Services.DomainEvents;
using Microsoft.Extensions.Caching.Memory;

namespace Aetheus.Back.Components.Mail;

/// <summary>PLAN-005: reconciles the mail rows of a server with the inventory of its latest heartbeat and
/// drops the cached mail reads when the reconciliation changed anything.</summary>
public sealed class MailInventoryReconciliationHandler(IMailStateReconciler reconciler, IMemoryCache cache)
    : IDomainEventHandler<MailInventoryReportedEvent>
{
    public async Task HandleAsync(MailInventoryReportedEvent domainEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var result = await reconciler.ReconcileAsync(domainEvent.ServerId, domainEvent.Mail, ct).ConfigureAwait(false);
        if (result.HasChanges)
            MailCacheKeys.Invalidate(cache, domainEvent.ServerId);
    }
}
