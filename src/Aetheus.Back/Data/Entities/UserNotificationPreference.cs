// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// A user's saved choice for one notification event type.
///
/// One row per (user, event type) rather than one boolean column per type on <see cref="User"/>: the
/// event catalogue (<c>NotificationEventTypes</c>) grows with the code, and a column set would need a
/// migration for every new type. With rows, a type the user never saved simply has no row and takes the
/// catalogue default, and the dispatch query can filter recipients on it in the database.
/// </summary>
public class UserNotificationPreference
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = default!;
}
