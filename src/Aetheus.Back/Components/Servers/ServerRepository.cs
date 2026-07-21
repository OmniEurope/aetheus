// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Shared;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Servers;

public class ServerRepository(AppDbContext db, TimeProvider timeProvider) : IServerRepository
{
    public async Task<(List<Server> Items, int TotalCount)> GetServersPagedAsync(
        string? search, string? sortBy, bool sortDescending,
        ServerType? type, ServerStatus? status,
        int page, int pageSize, List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var query = BuildFilteredSortedServerQuery(search, sortBy, sortDescending, type, status, accessibleIds);

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task<(List<ServerDto> Items, int TotalCount)> GetServersPagedProjectedAsync(
        string? search, string? sortBy, bool sortDescending,
        ServerType? type, ServerStatus? status,
        int page, int pageSize, List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var query = BuildFilteredSortedServerQuery(search, sortBy, sortDescending, type, status, accessibleIds);

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);

        // Project to an anonymous type first (Tags is a JSON string that must be
        // deserialized in memory), then map to ServerDto after materialization.
        var projected = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Hostname,
                s.OsDescription,
                s.AgentVersion,
                s.Status,
                s.Type,
                s.LastHeartbeat,
                s.Tags,
                s.CreatedAt,
                s.OrganizationId,
                s.AgentInstalledAt,
                s.PipelineRunnerEnabled,
                s.DockerAvailable,
                s.RequireContainerIsolation,
                s.InsecureTls,
                s.CapabilityDiagnosticsJson
            })
            .ToListAsync(ct).ConfigureAwait(false);

        var items = projected.Select(s => new ServerDto
        {
            Id = s.Id,
            Name = s.Name,
            Hostname = s.Hostname,
            OsDescription = s.OsDescription,
            AgentVersion = s.AgentVersion,
            Status = s.Status,
            Type = s.Type,
            LastHeartbeat = s.LastHeartbeat,
            Tags = TagsHelper.DeserializeTags(s.Tags),
            CreatedAt = s.CreatedAt,
            OrganizationId = s.OrganizationId,
            AgentInstalledAt = s.AgentInstalledAt,
            PipelineRunnerEnabled = s.PipelineRunnerEnabled,
            DockerAvailable = s.DockerAvailable,
            RequireContainerIsolation = s.RequireContainerIsolation,
            InsecureTls = s.InsecureTls,
            CapabilityDiagnostics = ServerDataMapper.DeserializeDiagnostics(s.CapabilityDiagnosticsJson)
        }).ToList();

        return (items, totalCount);
    }

    // Shared filter + sort pipeline for the two paged list overloads (entity vs. projected).
    // Identical WHERE/ORDER BY clauses live here so they cannot drift apart.
    private IQueryable<Server> BuildFilteredSortedServerQuery(
        string? search, string? sortBy, bool sortDescending,
        ServerType? type, ServerStatus? status, List<int>? accessibleIds)
    {
        var query = db.Servers.AsNoTracking().AsQueryable();

        if (accessibleIds is not null)
            query = query.Where(s => accessibleIds.Contains(s.Id));

        if (type.HasValue)
            query = query.Where(s => s.Type == type.Value);

        if (status.HasValue)
            query = query.Where(s => s.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(s => s.Name.Contains(search)
                || s.Hostname.Contains(search)
                || s.Tags.Contains(search));

        return sortBy?.ToLowerInvariant() switch
        {
            "name" => sortDescending ? query.OrderByDescending(s => s.Name) : query.OrderBy(s => s.Name),
            "status" => sortDescending ? query.OrderByDescending(s => s.Status) : query.OrderBy(s => s.Status),
            "lastheartbeat" => sortDescending ? query.OrderByDescending(s => s.LastHeartbeat) : query.OrderBy(s => s.LastHeartbeat),
            // Default (no column header chosen): online servers first, then most-recent heartbeat first
            // - operators want the live fleet at the top and the freshest signal leading within it.
            _ => query
                .OrderBy(s => s.Status == ServerStatus.Online ? 0 : 1)
                .ThenByDescending(s => s.LastHeartbeat)
        };
    }

    public async Task<List<DateTime>> GetRecentMetricTimestampsAsync(int serverId, DateTime since, CancellationToken ct = default)
    {
        return await db.ServerMetrics
            .AsNoTracking()
            .Where(m => m.ServerId == serverId && m.Timestamp >= since)
            .OrderBy(m => m.Timestamp)
            .Select(m => m.Timestamp)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<Server?> GetServerDetailAsync(int id, CancellationToken ct = default)
    {
        return await db.Servers
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Include(s => s.Services)
            .Include(s => s.DockerContainers)
            .Include(s => s.DockerImages)
            .Include(s => s.DockerComposeStacks)
            .Include(s => s.DockerNetworks)
            .Include(s => s.DockerVolumes)
            .Include(s => s.ApacheState)
            .Include(s => s.ApacheModules)
            .Include(s => s.ApacheVirtualHosts)
            .Include(s => s.CertbotCertificates)
            .Include(s => s.MailState)
            .Include(s => s.TeamspeakState)
            .Include(s => s.PortsentryState)
            .Include(s => s.RkhunterState)
            .Include(s => s.RkhunterWarnings)
            .Include(s => s.Tasks.OrderByDescending(t => t.CreatedAt).Take(10))
            .Include(s => s.Metrics.OrderByDescending(m => m.Timestamp).Take(1))
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public Task<Server?> GetServerWithCollectionsAsync(int id, CancellationToken ct = default)
        => GetServerDetailAsync(id, ct);

    public async Task<Server?> FindServerAsync(int id, CancellationToken ct = default)
    {
        return await db.Servers.FindAsync([id], ct).ConfigureAwait(false);
    }

    public async Task<Server?> FindServerWithTokensAsync(int id, CancellationToken ct = default)
    {
        // WHERE BEFORE Include (per architecture rules) - this is a single-row
        // load anyway, but stays consistent with the rest of the repo.
        return await db.Servers
            .Where(s => s.Id == id)
            .Include(s => s.Tokens)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task RemoveServerAsync(Server server, CancellationToken ct = default)
    {
        // Relying solely on FK CASCADE was too slow on servers with months of
        // metrics history - the single cascading transaction took long enough
        // that the browser surfaced it as a 'NetworkError' (the fetch gave up
        // before the response came back). On relational providers we set-delete
        // the heavy leaf tables first; the remaining small relations cascade.
        // InMemory (used by integration tests) still cascades via EF - it does
        // not support ExecuteDelete.
        if (db.Database.IsRelational())
        {
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(2));
            var id = server.Id;

            // Atomicity: the leaf set-deletes and the final server delete must all
            // commit together, so a mid-sequence failure can't leave orphaned rows.
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            // Restrict-FK: Vault → ProjectServer → Server - delete leaf-to-root.
            var psIds = await db.ProjectServers.Where(ps => ps.ServerId == id).Select(ps => ps.Id).ToListAsync(ct).ConfigureAwait(false);
            if (psIds.Count > 0)
            {
                await db.VaultSecretVersions.Where(v => v.VaultSecret!.Vault!.ProjectServerId != null && psIds.Contains(v.VaultSecret.Vault.ProjectServerId!.Value)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await db.VaultSecrets.Where(v => v.Vault.ProjectServerId != null && psIds.Contains(v.Vault.ProjectServerId!.Value)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await db.Vaults.Where(v => v.ProjectServerId != null && psIds.Contains(v.ProjectServerId!.Value)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await db.EnvironmentProjectServers.Where(x => psIds.Contains(x.ProjectServerId)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await db.ProjectServers.Where(ps => ps.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            }
            await db.ServerMetrics.Where(m => m.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.ServiceInfos.Where(s => s.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.DockerContainers.Where(x => x.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.DockerImages.Where(x => x.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.DockerNetworks.Where(x => x.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.DockerVolumes.Where(x => x.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.DockerComposeStacks.Where(x => x.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.ApacheStates.Where(x => x.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.ApacheModules.Where(x => x.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.ApacheVirtualHosts.Where(x => x.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.CertbotCertificates.Where(x => x.ServerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);

            db.Servers.Remove(server);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return;
        }
        db.Servers.Remove(server);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public Task AddMetricAsync(ServerMetric metric, CancellationToken ct = default)
    {
        db.ServerMetrics.Add(metric);
        return Task.CompletedTask;
    }

    public async Task<(int PreviousBlockedCount, int PreviousWarningCount)> GetSecurityCountersAsync(int serverId, CancellationToken ct = default)
    {
        var blockedCount = await db.PortsentryStates
            .AsNoTracking()
            .Where(s => s.ServerId == serverId)
            .Select(s => (int?)s.BlockedCount)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? 0;

        var warningCount = await db.RkhunterStates
            .AsNoTracking()
            .Where(s => s.ServerId == serverId)
            .Select(s => (int?)s.WarningCount)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? 0;

        return (blockedCount, warningCount);
    }

    public async Task ReplaceServicesAsync(int serverId, List<ServiceInfo> services, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
            await db.ServiceInfos.Where(s => s.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        else
        {
            var old = await db.ServiceInfos.Where(s => s.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.ServiceInfos.RemoveRange(old);
        }
        if (services.Count > 0)
            db.ServiceInfos.AddRange(services);
    }

    public async Task ReplaceDockerContainersAsync(int serverId, List<DockerContainer> containers, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
            await db.DockerContainers.Where(c => c.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        else
        {
            var old = await db.DockerContainers.Where(c => c.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.DockerContainers.RemoveRange(old);
        }
        if (containers.Count > 0)
            db.DockerContainers.AddRange(containers);
    }

    public async Task ReplaceDockerImagesAsync(int serverId, List<DockerImage> images, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
            await db.DockerImages.Where(i => i.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        else
        {
            var old = await db.DockerImages.Where(i => i.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.DockerImages.RemoveRange(old);
        }
        if (images.Count > 0)
            db.DockerImages.AddRange(images);
    }

    public async Task ReplaceDockerComposeStacksAsync(int serverId, List<DockerComposeStack> stacks, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
            await db.DockerComposeStacks.Where(s => s.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        else
        {
            var old = await db.DockerComposeStacks.Where(s => s.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.DockerComposeStacks.RemoveRange(old);
        }
        if (stacks.Count > 0)
            db.DockerComposeStacks.AddRange(stacks);
    }

    public async Task ReplaceDockerNetworksAsync(int serverId, List<DockerNetwork> networks, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
            await db.DockerNetworks.Where(n => n.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        else
        {
            var old = await db.DockerNetworks.Where(n => n.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.DockerNetworks.RemoveRange(old);
        }
        if (networks.Count > 0)
            db.DockerNetworks.AddRange(networks);
    }

    public async Task ReplaceDockerVolumesAsync(int serverId, List<DockerVolume> volumes, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
            await db.DockerVolumes.Where(v => v.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        else
        {
            var old = await db.DockerVolumes.Where(v => v.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.DockerVolumes.RemoveRange(old);
        }
        if (volumes.Count > 0)
            db.DockerVolumes.AddRange(volumes);
    }

    public async Task ReplaceApacheDataAsync(int serverId, ApacheState? state, List<ApacheModule> modules, List<ApacheVirtualHost> vhosts, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
        {
            await db.ApacheStates.Where(s => s.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.ApacheModules.Where(m => m.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.ApacheVirtualHosts.Where(v => v.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }
        else
        {
            var oldStates = await db.ApacheStates.Where(s => s.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.ApacheStates.RemoveRange(oldStates);
            var oldModules = await db.ApacheModules.Where(m => m.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.ApacheModules.RemoveRange(oldModules);
            var oldVhosts = await db.ApacheVirtualHosts.Where(v => v.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.ApacheVirtualHosts.RemoveRange(oldVhosts);
        }

        if (state is not null)
            db.ApacheStates.Add(state);
        if (modules.Count > 0)
            db.ApacheModules.AddRange(modules);
        if (vhosts.Count > 0)
            db.ApacheVirtualHosts.AddRange(vhosts);
    }

    public async Task ReplaceCertbotCertificatesAsync(int serverId, List<CertbotCertificate> certificates, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
            await db.CertbotCertificates.Where(c => c.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        else
        {
            var old = await db.CertbotCertificates.Where(c => c.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.CertbotCertificates.RemoveRange(old);
        }
        if (certificates.Count > 0)
            db.CertbotCertificates.AddRange(certificates);
    }

    public async Task ReplaceMailDataAsync(int serverId, MailState? state, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
            await db.MailStates.Where(s => s.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        else
        {
            var old = await db.MailStates.Where(s => s.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.MailStates.RemoveRange(old);
        }

        if (state is not null)
            db.MailStates.Add(state);
    }

    public async Task ReplaceTeamspeakDataAsync(
        int serverId, TeamspeakState? state, List<TeamspeakChannel> channels,
        List<TeamspeakClient> clients, List<TeamspeakBan> bans,
        CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
        {
            await db.TeamspeakStates.Where(s => s.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.TeamspeakChannels.Where(c => c.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.TeamspeakClients.Where(client => client.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.TeamspeakBans.Where(ban => ban.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }
        else
        {
            var oldStates = await db.TeamspeakStates.Where(s => s.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.TeamspeakStates.RemoveRange(oldStates);
            var oldChannels = await db.TeamspeakChannels.Where(c => c.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.TeamspeakChannels.RemoveRange(oldChannels);
            var oldClients = await db.TeamspeakClients.Where(client => client.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.TeamspeakClients.RemoveRange(oldClients);
            var oldBans = await db.TeamspeakBans.Where(ban => ban.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.TeamspeakBans.RemoveRange(oldBans);
        }

        if (state is not null)
            db.TeamspeakStates.Add(state);
        if (channels.Count > 0)
            db.TeamspeakChannels.AddRange(channels);
        if (clients.Count > 0)
            db.TeamspeakClients.AddRange(clients);
        if (bans.Count > 0)
            db.TeamspeakBans.AddRange(bans);
    }

    public async Task ReplacePortsentryDataAsync(int serverId, PortsentryState? state, List<PortsentryBlockedIp> blockedIps, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
        {
            await db.PortsentryStates.Where(s => s.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.PortsentryBlockedIps.Where(ip => ip.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }
        else
        {
            var oldStates = await db.PortsentryStates.Where(s => s.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.PortsentryStates.RemoveRange(oldStates);
            var oldIps = await db.PortsentryBlockedIps.Where(ip => ip.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.PortsentryBlockedIps.RemoveRange(oldIps);
        }

        if (state is not null)
            db.PortsentryStates.Add(state);
        if (blockedIps.Count > 0)
            db.PortsentryBlockedIps.AddRange(blockedIps);
    }

    public async Task ReplaceRkhunterDataAsync(int serverId, RkhunterState? state, List<RkhunterWarning> warnings, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
        {
            await db.RkhunterStates.Where(s => s.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.RkhunterWarnings.Where(w => w.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }
        else
        {
            var oldStates = await db.RkhunterStates.Where(s => s.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.RkhunterStates.RemoveRange(oldStates);
            var oldWarnings = await db.RkhunterWarnings.Where(w => w.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.RkhunterWarnings.RemoveRange(oldWarnings);
        }

        if (state is not null)
            db.RkhunterStates.Add(state);
        if (warnings.Count > 0)
            db.RkhunterWarnings.AddRange(warnings);
    }

    public async Task ReplaceSecurityUpdatesDataAsync(int serverId, SecurityUpdatesState? state, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
        {
            await db.SecurityUpdatesStates.Where(s => s.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }
        else
        {
            var old = await db.SecurityUpdatesStates.Where(s => s.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.SecurityUpdatesStates.RemoveRange(old);
        }

        if (state is not null)
            db.SecurityUpdatesStates.Add(state);
    }

    public async Task<SecurityUpdatesState?> GetSecurityUpdatesStateAsync(int serverId, CancellationToken ct = default)
        => await db.SecurityUpdatesStates.AsNoTracking()
            .FirstOrDefaultAsync(s => s.ServerId == serverId, ct).ConfigureAwait(false);

    public async Task ReplaceFirewallDataAsync(int serverId, FirewallState? state, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
        {
            await db.FirewallStates.Where(s => s.ServerId == serverId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }
        else
        {
            var old = await db.FirewallStates.Where(s => s.ServerId == serverId).ToListAsync(ct).ConfigureAwait(false);
            db.FirewallStates.RemoveRange(old);
        }

        if (state is not null)
            db.FirewallStates.Add(state);
    }

    public async Task<FirewallState?> GetFirewallStateAsync(int serverId, CancellationToken ct = default)
        => await db.FirewallStates.AsNoTracking()
            .FirstOrDefaultAsync(s => s.ServerId == serverId, ct).ConfigureAwait(false);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> ServerExistsAsync(int serverId, CancellationToken ct = default)
    {
        return await db.Servers
            .AnyAsync(s => s.Id == serverId, ct)
            .ConfigureAwait(false);
    }

    public async Task<List<Server>> GetStaleOnlineServersAsync(TimeSpan threshold, CancellationToken ct = default)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime - threshold;
        return await db.Servers
            .Where(s => s.Status == ServerStatus.Online && s.LastHeartbeat < cutoff)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> DeleteMetricsOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        // Set-based delete. The old load-all → RemoveRange → SaveChanges path
        // materialised the entire expired metrics table into memory every
        // cleanup cycle (heartbeats accrue fast across the fleet).
        if (db.Database.IsRelational())
        {
            return await db.ServerMetrics
                .Where(m => m.Timestamp < cutoff)
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);
        }

        // InMemory fallback - ExecuteDelete not supported
        var expired = await db.ServerMetrics
            .Where(m => m.Timestamp < cutoff)
            .ToListAsync(ct).ConfigureAwait(false);
        db.ServerMetrics.RemoveRange(expired);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return expired.Count;
    }

    public async Task<IReadOnlyList<DateTime>> GetMetricTimestampsAsync(int serverId, DateTime since, CancellationToken ct = default)
    {
        // WHERE before any projection - keeps the index seek straightforward.
        // The result is bounded by the (already enforced) retention sweep.
        // AsNoTracking applies to the entity query (before the Select projection).
        return await db.ServerMetrics
            .AsNoTracking()
            .Where(m => m.ServerId == serverId && m.Timestamp >= since)
            .OrderBy(m => m.Timestamp)
            .Select(m => m.Timestamp)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<string>> GetServerNamesAsync(List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var query = db.Servers.AsNoTracking();
        if (accessibleIds is not null) query = query.Where(s => accessibleIds.Contains(s.Id));
        return await query
            .OrderBy(s => s.Name)
            .Select(s => s.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public Task<List<string>> GetServerNamesAsync(CancellationToken ct = default)
        => GetServerNamesAsync(null, ct);

    public async Task<List<(int Id, string Name)>> GetServerIdNamePairsAsync(List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var query = db.Servers.AsNoTracking();
        if (accessibleIds is not null) query = query.Where(s => accessibleIds.Contains(s.Id));
        var rows = await query
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.Select(r => (r.Id, r.Name)).ToList();
    }

    public async Task<List<int>> GetProjectIdsForServerAsync(int serverId, CancellationToken ct = default)
    {
        return await ProjectIdsForServer(serverId)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<int>> GetPipelineIdsForServerAsync(int serverId, CancellationToken ct = default)
    {
        return await PipelineIdsForServer(serverId)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<Project>> GetProjectsForServerAsync(int serverId, CancellationToken ct = default)
    {
        return await db.Projects
            .Where(p => ProjectIdsForServer(serverId).Contains(p.Id))
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<Pipeline>> GetPipelinesForServerAsync(int serverId, CancellationToken ct = default)
    {
        // S-TECH-P2WX: include Runs so the service can resolve LastRunStatus/LastRunAt - the
        // server-scoped list previously showed "-" for "Last run" even when runs existed.
        return await db.Pipelines
            .Where(p => PipelineIdsForServer(serverId).Contains(p.Id))
            .Include(p => p.Project)
            .Include(p => p.Runs)
            .AsNoTracking()
            .AsSplitQuery()
            .OrderBy(p => p.Name)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<VariableLibrary>> GetVariableLibrariesForServerAsync(int serverId, CancellationToken ct = default)
    {
        return await db.VariableLibraries
            .Where(v => v.ProjectId != null && ProjectIdsForServer(serverId).Contains(v.ProjectId.Value))
            .Include(v => v.Project)
            .Include(v => v.Entries)
            .AsNoTracking()
            .OrderBy(v => v.Name)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<Vault>> GetVaultsForServerAsync(int serverId, CancellationToken ct = default)
    {
        return await db.Vaults
            .Where(v => v.ProjectId != null && ProjectIdsForServer(serverId).Contains(v.ProjectId.Value))
            .Include(v => v.Project)
            .Include(v => v.Secrets)
            .AsNoTracking()
            .OrderBy(v => v.Name)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<Release>> GetReleasesForServerAsync(int serverId, CancellationToken ct = default)
    {
        return await db.Releases
            .Where(r => ProjectIdsForServer(serverId).Contains(r.ProjectId))
            .Include(r => r.Project)
            .AsNoTracking()
            // Same NULL ordering as ReleaseRepository: unpublished (null PublishedAt) releases LAST,
            // without the non-sargable COALESCE - keeps draft placement consistent across every view.
            .OrderByDescending(r => r.PublishedAt != null)
            .ThenByDescending(r => r.PublishedAt)
            .ThenByDescending(r => r.Id)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    private IQueryable<int> ProjectIdsForServer(int serverId)
    {
        return db.PipelineStepRuns
            .Where(psr => psr.ServerId == serverId)
            .Select(psr => psr.PipelineRun.Pipeline.ProjectId)
            .Where(pid => pid != null)
            .Select(pid => pid!.Value)
            .Distinct();
    }

    private IQueryable<int> PipelineIdsForServer(int serverId)
    {
        return db.PipelineStepRuns
            .Where(psr => psr.ServerId == serverId)
            .Select(psr => psr.PipelineRun.PipelineId)
            .Distinct();
    }

    public async Task<ServiceType?> GetServiceTypeAsync(int serverId, string serviceName, CancellationToken ct = default)
    {
        return await db.ServiceInfos
            .Where(s => s.ServerId == serverId && s.Name == serviceName)
            .Select(s => (ServiceType?)s.Type)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<(List<Project> Items, int Total)> GetProjectsForServerPagedAsync(
        int serverId, string? search, int page, int pageSize, string? sortBy, bool sortDescending,
        CancellationToken ct = default)
    {
        var query = db.Projects
            .Where(project => ProjectIdsForServer(serverId).Contains(project.Id))
            .AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(project =>
                EF.Functions.ILike(project.Name, pattern) ||
                EF.Functions.ILike(project.Description, pattern));
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        query = (sortBy?.Trim().ToLowerInvariant(), sortDescending) switch
        {
            ("status", false) => query.OrderBy(project => project.Status).ThenBy(project => project.Name).ThenBy(project => project.Id),
            ("status", true) => query.OrderByDescending(project => project.Status).ThenBy(project => project.Name).ThenBy(project => project.Id),
            ("createdat", false) => query.OrderBy(project => project.CreatedAt).ThenBy(project => project.Id),
            ("createdat", true) => query.OrderByDescending(project => project.CreatedAt).ThenBy(project => project.Id),
            (_, true) => query.OrderByDescending(project => project.Name).ThenBy(project => project.Id),
            _ => query.OrderBy(project => project.Name).ThenBy(project => project.Id)
        };
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        return (items, total);
    }

    public async Task AddTaskAsync(ServerTask task, CancellationToken ct = default)
    {
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task AddTasksAsync(IEnumerable<ServerTask> tasks, CancellationToken ct = default)
    {
        db.Tasks.AddRange(tasks);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<(List<ServerTask> Items, int TotalCount)> GetTasksPagedAsync(int serverId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Tasks
            .Where(t => t.ServerId == serverId)
            .AsNoTracking();

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task<(List<TaskLog> Items, int TotalCount)> GetLogsPagedAsync(int serverId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.TaskLogs
            .Where(l => l.Task.ServerId == serverId)
            .AsNoTracking();

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(l => l.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return (items, totalCount);
    }
}
