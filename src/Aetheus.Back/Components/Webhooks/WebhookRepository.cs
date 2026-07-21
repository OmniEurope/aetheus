// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Webhooks;

public class WebhookRepository(AppDbContext db) : IWebhookRepository
{
    public async Task<List<WebhookSubscription>> GetAllAsync(CancellationToken ct = default)
    {
        return await db.WebhookSubscriptions
            .AsNoTracking()
            .OrderBy(w => w.EventType)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<WebhookSubscription>> GetEnabledByEventAsync(string eventType, CancellationToken ct = default)
    {
        // Tracking is intentional here: FireEventAsync mutates FailureCount on the returned entities.
        return await db.WebhookSubscriptions
            .Where(w => w.IsEnabled && w.EventType == eventType)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<WebhookSubscription?> FindAsync(int id, CancellationToken ct = default)
    {
        return await db.WebhookSubscriptions.FindAsync([id], ct).ConfigureAwait(false);
    }

    public async Task AddAsync(WebhookSubscription subscription, CancellationToken ct = default)
    {
        db.WebhookSubscriptions.Add(subscription);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(WebhookSubscription subscription, CancellationToken ct = default)
    {
        db.WebhookSubscriptions.Remove(subscription);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
