// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Notifications;

public interface INotificationRepository
{
    Task<(List<NotificationChannel> Items, int Total)> GetChannelsPagedAsync(
        string? search, int page, int pageSize, string? sortBy, bool sortDescending,
        CancellationToken ct = default);
    Task<NotificationChannel?> GetChannelWithRulesAsync(int id, CancellationToken ct = default);
    Task<NotificationChannel?> FindChannelAsync(int id, CancellationToken ct = default);
    Task AddChannelAsync(NotificationChannel channel, CancellationToken ct = default);
    Task RemoveChannelAsync(NotificationChannel channel, CancellationToken ct = default);
    Task<List<NotificationRule>> GetRulesForEventAsync(string eventType, CancellationToken ct = default);
    Task<(List<NotificationRule> Items, int Total)> GetRulesPagedAsync(
        string? search, int page, int pageSize, string? sortBy, bool sortDescending,
        CancellationToken ct = default);
    Task<NotificationRule?> FindRuleAsync(int id, CancellationToken ct = default);
    Task AddRuleAsync(NotificationRule rule, CancellationToken ct = default);
    Task RemoveRuleAsync(NotificationRule rule, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
