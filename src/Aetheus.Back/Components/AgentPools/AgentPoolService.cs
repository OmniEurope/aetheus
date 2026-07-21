// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.AgentPools;

public class AgentPoolService(
    IAgentPoolRepository repo,
    IAuditService audit,
    IEntityChangeNotifier notifier,
    TimeProvider timeProvider) : IAgentPoolService
{
    public async Task<PaginatedResult<AgentPoolDto>> GetPoolsAsync(PaginationRequest request, List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var (page, pageSize) = request.Normalize();
        var (items, totalCount) = await repo.GetPoolsPagedAsync(
            request.Search, page, pageSize, accessibleIds, ct).ConfigureAwait(false);

        return new PaginatedResult<AgentPoolDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<AgentPoolDto?> GetPoolAsync(int id, CancellationToken ct = default)
    {
        var pool = await repo.GetPoolWithServersAsync(id, ct).ConfigureAwait(false);
        return pool is null ? null : MapToDto(pool);
    }

    public async Task<AgentPoolDto> CreatePoolAsync(CreateAgentPoolRequest request, CancellationToken ct = default)
    {
        var pool = new AgentPool
        {
            Name = request.Name,
            Description = request.Description,
            MaxConcurrency = request.MaxConcurrency,
            Servers = request.ServerIds.Select(sid => new AgentPoolServer { ServerId = sid }).ToList()
        };

        await repo.AddPoolAsync(pool, ct).ConfigureAwait(false);
        await audit.LogAsync("Created", "AgentPool", pool.Id, pool.Name, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.AgentPool, pool.Id, EntityChangeOps.Created, ct).ConfigureAwait(false);

        var result = await repo.GetPoolWithServersAsync(pool.Id, ct).ConfigureAwait(false);
        return MapToDto(result!);
    }

    public async Task<AgentPoolDto?> UpdatePoolAsync(int id, UpdateAgentPoolRequest request, CancellationToken ct = default)
    {
        var pool = await repo.FindPoolAsync(id, ct).ConfigureAwait(false);
        if (pool is null) return null;

        pool.Name = request.Name;
        pool.Description = request.Description;
        pool.MaxConcurrency = request.MaxConcurrency;
        pool.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        pool.Servers.Clear();
        pool.Servers.AddRange(request.ServerIds.Select(sid => new AgentPoolServer { AgentPoolId = id, ServerId = sid }));

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.LogAsync("Updated", "AgentPool", pool.Id, pool.Name, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.AgentPool, pool.Id, EntityChangeOps.Updated, ct).ConfigureAwait(false);

        var result = await repo.GetPoolWithServersAsync(id, ct).ConfigureAwait(false);
        return MapToDto(result!);
    }

    public async Task<bool> DeletePoolAsync(int id, CancellationToken ct = default)
    {
        var pool = await repo.FindPoolAsync(id, ct).ConfigureAwait(false);
        if (pool is null) return false;

        var name = pool.Name;
        await repo.RemovePoolAsync(pool, ct).ConfigureAwait(false);
        await audit.LogAsync("Deleted", "AgentPool", id, name, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.AgentPool, id, EntityChangeOps.Deleted, ct).ConfigureAwait(false);
        return true;
    }

    private static AgentPoolDto MapToDto(AgentPool p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        MaxConcurrency = p.MaxConcurrency,
        Servers = p.Servers.Select(ps => new AgentPoolServerDto
        {
            ServerId = ps.ServerId,
            ServerName = ps.Server?.Name ?? string.Empty,
            ServerStatus = ps.Server?.Status ?? ServerStatus.Offline
        }).ToList(),
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt
    };
}
