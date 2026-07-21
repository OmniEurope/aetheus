// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Webhooks;

public interface IWebhookRepository
{
    Task<List<WebhookSubscription>> GetAllAsync(CancellationToken ct = default);
    Task<List<WebhookSubscription>> GetEnabledByEventAsync(string eventType, CancellationToken ct = default);
    Task<WebhookSubscription?> FindAsync(int id, CancellationToken ct = default);
    Task AddAsync(WebhookSubscription subscription, CancellationToken ct = default);
    Task RemoveAsync(WebhookSubscription subscription, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
