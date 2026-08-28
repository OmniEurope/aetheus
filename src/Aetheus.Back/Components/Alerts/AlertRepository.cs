// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Npgsql;

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

    public async Task<int> AddProvisionedRulesIfMissingAsync(
        IReadOnlyCollection<AlertRule> rules,
        CancellationToken ct = default)
    {
        if (rules.Count == 0) return 0;

        // Provisioning runs in every backend replica. Use the unique provisioning key as the
        // concurrency arbiter while staying on the EF repository boundary: a losing replica
        // detaches its colliding entity and resumes with the next independently saved baseline.
        var inserted = 0;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var rule in rules
                     .Where(rule => rule.ProvisioningKey is not null)
                     .OrderBy(rule => rule.ProvisioningKey, StringComparer.Ordinal))
        {
            if (await db.AlertRules
                    .AnyAsync(existing => existing.ProvisioningKey == rule.ProvisioningKey, ct)
                    .ConfigureAwait(false))
            {
                continue;
            }

            rule.CreatedAt = now;
            rule.UpdatedAt = now;
            db.AlertRules.Add(rule);
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                inserted++;
            }
            catch (DbUpdateException exception) when (IsPotentialProvisioningConflict(exception))
            {
                db.Entry(rule).State = EntityState.Detached;
                var concurrentWinnerExists = await db.AlertRules
                    .AsNoTracking()
                    .AnyAsync(
                        existing => existing.ProvisioningKey == rule.ProvisioningKey,
                        ct)
                    .ConfigureAwait(false);
                if (!concurrentWinnerExists)
                    throw;
            }
        }

        return inserted;
    }

    private static bool IsPotentialProvisioningConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_AlertRules_ProvisioningKey" or "IX_AlertRules_Name"
        };

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

    public async Task<IReadOnlyDictionary<int, List<ServerMetric>>> GetRecentMetricsForServersAsync(
        IReadOnlyCollection<int> serverIds,
        int seconds,
        CancellationToken ct = default)
    {
        if (serverIds.Count == 0)
            return new Dictionary<int, List<ServerMetric>>();

        var ids = serverIds.Distinct().ToArray();
        var since = timeProvider.GetUtcNow().UtcDateTime.AddSeconds(-seconds);
        var metrics = await db.Servers
            .AsNoTracking()
            .Where(server => ids.Contains(server.Id))
            .SelectMany(server => db.ServerMetrics
                .Where(metric => metric.ServerId == server.Id && metric.Timestamp >= since)
                .OrderByDescending(metric => metric.Timestamp)
                .Take(5000))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return metrics
            .GroupBy(metric => metric.ServerId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(metric => metric.Timestamp).ToList());
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
