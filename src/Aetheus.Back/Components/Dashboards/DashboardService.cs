// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Dashboards;

public class DashboardService(IDashboardRepository repo, IAuditService audit, TimeProvider timeProvider) : IDashboardService
{
    public async Task<List<DashboardDto>> GetUserDashboardsAsync(int userId, CancellationToken ct = default)
    {
        var dashboards = await repo.GetByUserIdAsync(userId, ct).ConfigureAwait(false);
        return dashboards.Select(MapToDto).ToList();
    }

    public async Task<DashboardDto?> GetDashboardAsync(int id, int userId, CancellationToken ct = default)
    {
        var dashboard = await repo.GetDetailAsync(id, ct).ConfigureAwait(false);
        // Ownership check - dashboards are user-scoped and must not leak across accounts (IDOR).
        if (dashboard is null || dashboard.UserId != userId) return null;
        return MapToDto(dashboard);
    }

    public async Task<DashboardDto> CreateDashboardAsync(int userId, CreateDashboardRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IsDefault)
            await repo.ClearDefaultsAsync(userId, ct).ConfigureAwait(false);

        var entity = new Dashboard
        {
            UserId = userId,
            Name = request.Name,
            IsDefault = request.IsDefault,
            Widgets = request.Widgets.Select(MapWidget).ToList()
        };

        await repo.AddAsync(entity, ct).ConfigureAwait(false);
        await audit.LogAsync("Created", "Dashboard", entity.Id, request.Name, ct).ConfigureAwait(false);
        return MapToDto(entity);
    }

    public async Task<DashboardDto?> UpdateDashboardAsync(int id, int userId, UpdateDashboardRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entity = await repo.FindAsync(id, ct).ConfigureAwait(false);
        if (entity is null || entity.UserId != userId) return null;

        if (request.RowVersion != entity.RowVersion)
            throw new ConflictException("The dashboard was modified by another user. Please reload and try again.");

        if (request.IsDefault && !entity.IsDefault)
            await repo.ClearDefaultsAsync(userId, ct).ConfigureAwait(false);

        entity.Name = request.Name;
        entity.IsDefault = request.IsDefault;
        entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        entity.RowVersion = Guid.NewGuid();

        // Replace widgets entirely
        entity.Widgets.Clear();
        foreach (var w in request.Widgets)
        {
            entity.Widgets.Add(MapWidget(w));
        }

        try
        {
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The dashboard was modified by another user. Please reload and try again.");
        }

        await audit.LogAsync("Updated", "Dashboard", id, null, ct).ConfigureAwait(false);
        return MapToDto(entity);
    }

    public async Task<bool> DeleteDashboardAsync(int id, int userId, CancellationToken ct = default)
    {
        var entity = await repo.FindAsync(id, ct).ConfigureAwait(false);
        if (entity is null || entity.UserId != userId) return false;

        await repo.RemoveAsync(entity, ct).ConfigureAwait(false);
        await audit.LogAsync("Deleted", "Dashboard", id, null, ct).ConfigureAwait(false);
        return true;
    }

    private static DashboardDto MapToDto(Dashboard d) => new()
    {
        Id = d.Id,
        UserId = d.UserId,
        Name = d.Name,
        IsDefault = d.IsDefault,
        Widgets = d.Widgets.Select(w => new DashboardWidgetDto
        {
            Id = w.Id,
            DashboardId = w.DashboardId,
            WidgetType = w.WidgetType,
            Title = w.Title,
            Column = w.Column,
            Row = w.Row,
            Width = w.Width,
            Height = w.Height,
            ConfigurationJson = w.ConfigurationJson,
            IsVisible = w.IsVisible
        }).ToList(),
        CreatedAt = d.CreatedAt,
        RowVersion = d.RowVersion
    };

    private static DashboardWidget MapWidget(CreateDashboardWidgetRequest widget) => new()
    {
        WidgetType = widget.WidgetType,
        Title = widget.Title,
        Column = widget.Column,
        Row = widget.Row,
        Width = widget.Width,
        Height = widget.Height,
        ConfigurationJson = widget.ConfigurationJson,
        IsVisible = widget.IsVisible
    };
}
