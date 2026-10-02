// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Notifications;

/// <summary>
/// State of one notification addressed to one user. The values are persisted as integers, so they
/// never move. <see cref="NotConfigured"/> is what a delivery records while no transport exists for it
/// (there is no SMTP transport today): the notification was produced for the user, and nothing left
/// the server. A delivery is only <see cref="Sent"/> once a real transport has accepted it.
/// </summary>
public enum NotificationDeliveryStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    NotConfigured = 3
}
