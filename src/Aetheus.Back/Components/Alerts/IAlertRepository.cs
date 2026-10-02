// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Alerts;

public interface IAlertRepository
{
    Task<List<AlertRule>> GetAllAsync(CancellationToken ct = default);
    Task<List<AlertRule>> GetEnabledAsync(CancellationToken ct = default);
    Task<AlertRule?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<AlertRule?> FindAsync(int id, CancellationToken ct = default);
    Task AddAsync(AlertRule rule, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<AlertRule> rules, CancellationToken ct = default);
    /// <summary>Every server's id and name, ordered by name: an own read of the shared Servers table (the
    /// Servers module sits above Alerts, layer guard 2026-09-25).</summary>
    Task<List<(int Id, string Name)>> GetServerIdNamePairsAsync(CancellationToken ct = default);

    Task<int> AddProvisionedRulesIfMissingAsync(
        IReadOnlyCollection<AlertRule> rules,
        CancellationToken ct = default);
    Task RemoveAsync(AlertRule rule, CancellationToken ct = default);
    Task<List<ServerMetric>> GetRecentMetricsAsync(int serverId, int seconds, CancellationToken ct = default);
    Task<IReadOnlyDictionary<int, List<ServerMetric>>> GetRecentMetricsForServersAsync(
        IReadOnlyCollection<int> serverIds,
        int seconds,
        CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
