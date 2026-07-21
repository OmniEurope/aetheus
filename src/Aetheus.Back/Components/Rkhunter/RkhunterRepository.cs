// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Rkhunter;

public class RkhunterRepository(AppDbContext db, TimeProvider timeProvider) : IRkhunterRepository
{
    public async Task<RkhunterState?> GetStateAsync(int serverId, CancellationToken ct = default)
    {
        return await db.RkhunterStates
            .AsNoTracking()
            .Where(r => r.ServerId == serverId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RkhunterWarning>> GetWarningsAsync(int serverId, bool includeArchived = false, CancellationToken ct = default)
    {
        var query = db.RkhunterWarnings
            .AsNoTracking()
            .Where(w => w.ServerId == serverId);

        if (!includeArchived)
            query = query.Where(w => !w.IsArchived);

        return await query
            .OrderByDescending(w => w.FoundAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RkhunterScanResult>> GetScanHistoryAsync(int serverId, int limit = 50, CancellationToken ct = default)
    {
        return await db.RkhunterScanResults
            .AsNoTracking()
            .Where(r => r.ServerId == serverId)
            .OrderByDescending(r => r.ScanTime)
            .Take(limit)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<RkhunterScanResult> AddScanResultAsync(RkhunterScanResult result, CancellationToken ct = default)
    {
        db.RkhunterScanResults.Add(result);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return result;
    }

    public async Task AddTaskAsync(ServerTask task, CancellationToken ct = default)
    {
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateScanScheduleAsync(int serverId, string? cronExpression, CancellationToken ct = default)
    {
        var state = await db.RkhunterStates
            .Where(r => r.ServerId == serverId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (state is null) return;

        state.ScanScheduleCron = cronExpression;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<RkhunterState>> GetScheduledStatesAsync(CancellationToken ct = default)
    {
        return await db.RkhunterStates
            .AsNoTracking()
            .Where(r => r.ScanScheduleCron != null && r.ScanScheduleCron != "")
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task UpdateLastScheduledScanAsync(int serverId, CancellationToken ct = default)
    {
        var state = await db.RkhunterStates
            .Where(r => r.ServerId == serverId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (state is null) return;

        state.LastScheduledScanAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
