// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Certbot;

/// <summary>
/// PLAN-007: a heartbeat carried a renewal rehearsal (<c>certbot renew --dry-run</c>) recorded since the
/// previous one, and it failed. Servers owns the heartbeat; the Certbot module decides how to notify.
/// </summary>
public sealed record CertbotRenewalCheckFailedEvent(int ServerId, DateTime CheckedAt) : IDomainEvent;
