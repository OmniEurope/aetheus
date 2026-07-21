// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Alerts;

public class AlertRepository(AppDbContext db, TimeProvider timeProvider) : IAlertRepository
{
    public async Task<List<AlertRule>> GetAllAsync(CancellationToken ct = default)
    {
        return await db.AlertRules
            .AsNoTracking()
            .Include(r => r.Server)
            .Include(r => r.NotificationChannel)
            .OrderBy(r => r.Name)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<AlertRule>> GetEnabledAsync(CancellationToken ct = default)
    {
        return await db.AlertRules
            .Where(r => r.IsEnabled)
            .Include(r => r.Server)
            .Include(r => r.NotificationChannel)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<AlertRule?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await db.AlertRules
            .AsNoTracking()
            .Include(r => r.Server)
            .Include(r => r.NotificationChannel)
            .FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
    }

    public async Task<AlertRule?> FindAsync(int id, CancellationToken ct = default)
    {
        return await db.AlertRules
            .Include(r => r.Server)
            .Include(r => r.NotificationChannel)
            .FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
    }

    public async Task AddAsync(AlertRule rule, CancellationToken ct = default)
    {
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task AddRangeAsync(IEnumerable<AlertRule> rules, CancellationToken ct = default)
    {
        db.AlertRules.AddRange(rules);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(AlertRule rule, CancellationToken ct = default)
    {
        db.AlertRules.Remove(rule);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<ServerMetric>> GetRecentMetricsAsync(int serverId, int seconds, CancellationToken ct = default)
    {
        var since = timeProvider.GetUtcNow().UtcDateTime.AddSeconds(-seconds);
        return await db.ServerMetrics
            .AsNoTracking()
            .Where(m => m.ServerId == serverId && m.Timestamp >= since)
            .OrderByDescending(m => m.Timestamp)
            // Hard guard against a rule with a very large sustained window materialising the whole
            // high-volume metrics slice; 5000 most-recent points is far beyond any real evaluation need.
            .Take(5000)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
