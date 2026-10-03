// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Servers.Events;

/// <summary>
/// Audit R2-023 follow-up: a server's sudoers drop-ins differ from their install baseline. Raised when a
/// drifted state is first seen and again every <see cref="SudoersDriftMonitor.ReminderInterval"/> while
/// it lasts (<paramref name="IsReminder"/>), so the alert is recorded where nobody can miss it, not only
/// pushed to the browsers connected at that moment.
/// </summary>
public sealed record SudoersDriftDetectedEvent(
    int ServerId,
    string ServerName,
    string Files,
    string Message,
    bool IsReminder,
    DateTime DetectedAt) : IDomainEvent;
