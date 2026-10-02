// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Servers.Events;

/// <summary>
/// PLAN-005: an agent heartbeat carried the mail inventory of an installed mail stack. Servers owns the
/// heartbeat and the <c>MailState</c> row; what the inventory means for mail domains, accounts and aliases
/// is decided by the Mail module (reconciliation), which keeps the module dependency one-way
/// (Mail depends on Servers, never the reverse). Published to the background queue: a reconciliation
/// failure must never fail the heartbeat that proved the agent alive.
/// </summary>
public sealed record MailInventoryReportedEvent(int ServerId, MailDataDto Mail) : IDomainEvent;
