// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Components.Servers;

public class ServerRetirementService(
    IServerRetirementRepository repo,
    IHubContext<ServerHub> serverHub,
    IAuditService audit,
    TimeProvider timeProvider) : IServerRetirementService
{
    public async Task<bool> RetireServerAsync(int id, string actorUsername, CancellationToken ct = default)
    {
        var server = await repo.FindServerIncludingRetiredAsync(id, ct).ConfigureAwait(false);
        if (server is null || server.DeletedAt is not null) return false;

        await repo.RetireAsync(server, timeProvider.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        await audit.LogAsync("Retired", "Server", id,
            $"{server.Name} retired by {actorUsername}; its links are kept and reinstalling the agent on the same machine restores it",
            ct).ConfigureAwait(false);
        // Same event as the former delete: every open list drops the row.
        await serverHub.Clients.Groups([HubGroups.AllServers, HubGroups.Server(id), HubGroups.ServerOrg(server.OrganizationId)])
            .SendAsync("ServerRemoved", id, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<PaginatedResult<RetiredServerDto>> GetRetiredServersAsync(
        PaginationRequest request, List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (page, pageSize) = request.Normalize();
        var (items, totalCount) = await repo.GetRetiredServersPagedAsync(
            request.Search, request.SortBy, request.SortDescending, page, pageSize, accessibleIds, ct, request.Filters).ConfigureAwait(false);
        return new PaginatedResult<RetiredServerDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<bool> PurgeServerAsync(int id, string actorUsername, CancellationToken ct = default)
    {
        var server = await repo.FindServerIncludingRetiredAsync(id, ct).ConfigureAwait(false);
        if (server is null) return false;
        if (server.DeletedAt is null)
            throw new ConflictException("An active server cannot be deleted permanently; retire it first.");

        var name = server.Name;
        var organizationId = server.OrganizationId;
        var backupPolicies = await repo.PurgeServerAsync(server, ct).ConfigureAwait(false);
        await audit.LogAsync("Purged", "Server", id,
            $"{name} permanently deleted by {actorUsername}, with every linked record and {backupPolicies} backup polic{(backupPolicies == 1 ? "y" : "ies")}",
            ct).ConfigureAwait(false);
        await serverHub.Clients.Groups([HubGroups.AllServers, HubGroups.Server(id), HubGroups.ServerOrg(organizationId)])
            .SendAsync("ServerRemoved", id, ct).ConfigureAwait(false);
        return true;
    }
}
