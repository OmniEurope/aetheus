// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Webhooks;

public interface IWebhookService
{
    Task<List<WebhookSubscriptionDto>> GetSubscriptionsAsync(CancellationToken ct = default);
    Task<WebhookSubscriptionDto?> GetSubscriptionAsync(int id, CancellationToken ct = default);
    Task<WebhookSubscriptionDto> CreateSubscriptionAsync(CreateWebhookSubscriptionRequest request, CancellationToken ct = default);
    Task<WebhookSubscriptionDto?> UpdateSubscriptionAsync(int id, UpdateWebhookSubscriptionRequest request, CancellationToken ct = default);
    Task<bool> DeleteSubscriptionAsync(int id, CancellationToken ct = default);
    Task FireEventAsync(string eventType, object payload, CancellationToken ct = default);
}
