// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Notifications;
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Certbot;

/// <summary>PLAN-007: a failed renewal rehearsal reaches the notification rules while the certificates
/// are still valid, instead of being discovered when one expires.</summary>
public sealed class CertbotRenewalCheckNotificationHandler(INotificationService notifications)
    : IDomainEventHandler<CertbotRenewalCheckFailedEvent>
{
    public const string EventType = "certbot.renewal-check.failed";

    public Task HandleAsync(CertbotRenewalCheckFailedEvent domainEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return notifications.SendEventAsync(EventType, new
        {
            domainEvent.ServerId,
            domainEvent.CheckedAt
        }, ct);
    }
}
