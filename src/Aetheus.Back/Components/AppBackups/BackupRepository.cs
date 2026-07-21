// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.AppBackups;

internal sealed class BackupRepository(AppDbContext db) : IBackupRepository
{
    public void AddPolicy(BackupPolicy policy) => db.BackupPolicies.Add(policy);
    public void DeletePolicy(BackupPolicy policy) => db.BackupPolicies.Remove(policy);

    public async Task<(List<BackupPolicy> Items, int TotalCount)> GetPoliciesPagedAsync(
        IReadOnlyCollection<int>? projectIds, string? search, string? sortBy,
        bool sortDescending, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.BackupPolicies.AsNoTracking().AsQueryable();
        if (projectIds is not null)
            query = query.Where(p => projectIds.Contains(p.ProjectId));
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search)
                || (p.Project != null && p.Project.Name.Contains(search))
                || (p.Server != null && p.Server.Name.Contains(search)));

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);
        var ordered = (sortBy, sortDescending) switch
        {
            ("ProjectName", false) => query.OrderBy(p => p.Project!.Name),
            ("ProjectName", true) => query.OrderByDescending(p => p.Project!.Name),
            ("ServerName", false) => query.OrderBy(p => p.Server!.Name),
            ("ServerName", true) => query.OrderByDescending(p => p.Server!.Name),
            ("DbEngine", false) => query.OrderBy(p => p.DbEngine),
            ("DbEngine", true) => query.OrderByDescending(p => p.DbEngine),
            ("ScheduleCron", false) => query.OrderBy(p => p.ScheduleCron),
            ("ScheduleCron", true) => query.OrderByDescending(p => p.ScheduleCron),
            ("RetentionCount", false) => query.OrderBy(p => p.RetentionCount),
            ("RetentionCount", true) => query.OrderByDescending(p => p.RetentionCount),
            ("LastRunAt", false) => query.OrderBy(p => p.LastRunAt),
            ("LastRunAt", true) => query.OrderByDescending(p => p.LastRunAt),
            ("Enabled", false) => query.OrderBy(p => p.Enabled),
            ("Enabled", true) => query.OrderByDescending(p => p.Enabled),
            ("Name", true) => query.OrderByDescending(p => p.Name),
            _ => query.OrderBy(p => p.Name)
        };

        var items = await ordered
            .Include(p => p.Project)
            .Include(p => p.Server)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return (items, totalCount);
    }

    public async Task<BackupPolicy?> FindPolicyAsync(int id, CancellationToken ct = default)
        => await db.BackupPolicies.FirstOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false);

    public async Task<List<BackupPolicy>> GetEnabledPoliciesAsync(CancellationToken ct = default)
        => await db.BackupPolicies
            .Where(p => p.Enabled)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public void AddRun(BackupRun run) => db.BackupRuns.Add(run);

    public async Task<BackupRun?> FindRunAsync(int id, CancellationToken ct = default)
        => await db.BackupRuns.FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);

    public async Task<BackupRun?> FindRunWithPolicyAsync(int id, CancellationToken ct = default)
        => await db.BackupRuns.Include(r => r.BackupPolicy)
            .FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);

    public async Task<(List<BackupRun> Items, int TotalCount)> GetRunsForPolicyPagedAsync(
        int policyId, string? search, string? sortBy, bool sortDescending,
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.BackupRuns.AsNoTracking().Where(r => r.BackupPolicyId == policyId);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(r => r.Message != null && r.Message.Contains(search));

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);
        var ordered = (sortBy, sortDescending) switch
        {
            ("Status", false) => query.OrderBy(r => r.Status),
            ("Status", true) => query.OrderByDescending(r => r.Status),
            ("RestoreCheckStatus", false) => query.OrderBy(r => r.RestoreCheckStatus),
            ("RestoreCheckStatus", true) => query.OrderByDescending(r => r.RestoreCheckStatus),
            ("SizeBytes", false) => query.OrderBy(r => r.SizeBytes),
            ("SizeBytes", true) => query.OrderByDescending(r => r.SizeBytes),
            ("StartedAt", false) => query.OrderBy(r => r.StartedAt),
            _ => query.OrderByDescending(r => r.StartedAt)
        };
        var items = await ordered.Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);
        return (items, totalCount);
    }

    public async Task<BackupRun?> GetLatestSuccessfulRunAsync(int policyId, CancellationToken ct = default)
        => await db.BackupRuns
            .Where(r => r.BackupPolicyId == policyId && r.Status == BackupRunStatus.Succeeded)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await db.SaveChangesAsync(ct).ConfigureAwait(false);
}
