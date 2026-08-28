// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Components.Alerts;

public class AlertService(IAlertRepository repo, IAuditService audit, IHubContext<AlertHub> alertHub, TimeProvider timeProvider) : IAlertService
{
    // Real-time: any rule CRUD pushes AlertRuleChanged so an admin's Alerts page reflects another
    // admin's create/edit/delete without waiting for the rule to fire (or a manual reload).
    private Task BroadcastRuleChangedAsync(int ruleId, CancellationToken ct) =>
        alertHub.Clients.Group(HubGroups.Alerts).SendAsync("AlertRuleChanged", ruleId, ct);

    public async Task<List<AlertRuleDto>> GetAlertRulesAsync(CancellationToken ct = default)
    {
        var rules = await repo.GetAllAsync(ct).ConfigureAwait(false);
        return rules.Select(MapToDto).ToList();
    }

    public async Task<AlertRuleDto?> GetAlertRuleAsync(int id, CancellationToken ct = default)
    {
        var rule = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
        return rule is null ? null : MapToDto(rule);
    }

    public async Task<AlertRuleDto> CreateAlertRuleAsync(CreateAlertRuleRequest request, CancellationToken ct = default)
    {
        var rule = new AlertRule
        {
            Name = request.Name,
            ServerId = request.ServerId,
            Metric = request.Metric,
            Operator = request.Operator,
            Threshold = request.Threshold,
            SustainedSeconds = request.SustainedSeconds,
            Severity = request.Severity,
            NotificationChannelId = request.NotificationChannelId
        };
        await repo.AddAsync(rule, ct).ConfigureAwait(false);
        await audit.LogAsync("Created", "AlertRule", rule.Id, rule.Name, ct).ConfigureAwait(false);

        // Reload with navigation properties
        var created = await repo.GetByIdAsync(rule.Id, ct).ConfigureAwait(false);
        await BroadcastRuleChangedAsync(rule.Id, ct).ConfigureAwait(false);
        return MapToDto(created!);
    }

    public async Task<AlertRuleDto?> UpdateAlertRuleAsync(int id, UpdateAlertRuleRequest request, CancellationToken ct = default)
    {
        var rule = await repo.FindAsync(id, ct).ConfigureAwait(false);
        if (rule is null) return null;

        rule.Name = request.Name;
        rule.ServerId = request.ServerId;
        rule.Metric = request.Metric;
        rule.Operator = request.Operator;
        rule.Threshold = request.Threshold;
        rule.SustainedSeconds = request.SustainedSeconds;
        rule.Severity = request.Severity;
        rule.IsEnabled = request.IsEnabled;
        rule.NotificationChannelId = request.NotificationChannelId;
        rule.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.LogAsync("Updated", "AlertRule", rule.Id, rule.Name, ct).ConfigureAwait(false);
        await BroadcastRuleChangedAsync(rule.Id, ct).ConfigureAwait(false);
        return MapToDto(rule);
    }

    public async Task<bool> DeleteAlertRuleAsync(int id, CancellationToken ct = default)
    {
        var rule = await repo.FindAsync(id, ct).ConfigureAwait(false);
        if (rule is null) return false;
        var name = rule.Name;
        await repo.RemoveAsync(rule, ct).ConfigureAwait(false);
        await audit.LogAsync("Deleted", "AlertRule", id, name, ct).ConfigureAwait(false);
        await BroadcastRuleChangedAsync(id, ct).ConfigureAwait(false);
        return true;
    }

    private static AlertRuleDto MapToDto(AlertRule r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        ServerId = r.ServerId,
        ServerName = r.Server?.Name,
        Metric = r.Metric,
        Operator = r.Operator,
        Threshold = r.Threshold,
        SustainedSeconds = r.SustainedSeconds,
        Severity = r.Severity,
        IsEnabled = r.IsEnabled,
        NotificationChannelId = r.NotificationChannelId,
        NotificationChannelName = r.NotificationChannel?.Name,
        LastTriggeredAt = r.LastTriggeredAt,
        CreatedAt = r.CreatedAt
    };
}
