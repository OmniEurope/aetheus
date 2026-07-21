// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Teamspeak;

public class TeamspeakRepository(AppDbContext db) : ITeamspeakRepository
{
    public async Task<TeamspeakState?> GetStateAsync(int serverId, CancellationToken ct = default)
    {
        return await db.TeamspeakStates
            .AsNoTracking()
            .Where(t => t.ServerId == serverId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<(List<TeamspeakChannel> Items, int Total)> GetChannelsPagedAsync(
        int serverId, string? search, int page, int pageSize, string? sortBy, bool sortDescending,
        CancellationToken ct = default)
    {
        var query = db.TeamspeakChannels
            .AsNoTracking()
            .Where(channel => channel.ServerId == serverId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(channel => EF.Functions.ILike(channel.Name, pattern));
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        query = (sortBy?.Trim().ToLowerInvariant(), sortDescending) switch
        {
            ("name", false) => query.OrderBy(channel => channel.Name).ThenBy(channel => channel.ChannelId),
            ("name", true) => query.OrderByDescending(channel => channel.Name).ThenBy(channel => channel.ChannelId),
            ("totalclients", false) => query.OrderBy(channel => channel.TotalClients).ThenBy(channel => channel.Name),
            ("totalclients", true) => query.OrderByDescending(channel => channel.TotalClients).ThenBy(channel => channel.Name),
            (_, true) => query.OrderByDescending(channel => channel.ParentId).ThenByDescending(channel => channel.Order),
            _ => query.OrderBy(channel => channel.ParentId).ThenBy(channel => channel.Order)
        };
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        return (items, total);
    }

    public async Task<(List<TeamspeakClient> Items, int Total)> GetClientsPagedAsync(
        int serverId, string? search, int page, int pageSize, string? sortBy, bool sortDescending,
        CancellationToken ct = default)
    {
        var query = db.TeamspeakClients
            .AsNoTracking()
            .Where(client => client.ServerId == serverId && !client.IsServerQuery);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(client =>
                EF.Functions.ILike(client.Nickname, pattern) ||
                EF.Functions.ILike(client.UniqueId, pattern) ||
                EF.Functions.ILike(client.Platform, pattern));
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        query = (sortBy?.Trim().ToLowerInvariant(), sortDescending) switch
        {
            ("channelid", false) => query.OrderBy(client => client.ChannelId).ThenBy(client => client.Nickname),
            ("channelid", true) => query.OrderByDescending(client => client.ChannelId).ThenBy(client => client.Nickname),
            ("platform", false) => query.OrderBy(client => client.Platform).ThenBy(client => client.Nickname),
            ("platform", true) => query.OrderByDescending(client => client.Platform).ThenBy(client => client.Nickname),
            (_, true) => query.OrderByDescending(client => client.Nickname).ThenBy(client => client.ClientId),
            _ => query.OrderBy(client => client.Nickname).ThenBy(client => client.ClientId)
        };
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        return (items, total);
    }

    public async Task<(List<TeamspeakBan> Items, int Total)> GetBansPagedAsync(
        int serverId, string? search, int page, int pageSize, string? sortBy, bool sortDescending,
        CancellationToken ct = default)
    {
        var query = db.TeamspeakBans
            .AsNoTracking()
            .Where(ban => ban.ServerId == serverId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(ban =>
                EF.Functions.ILike(ban.Nickname, pattern) ||
                EF.Functions.ILike(ban.UniqueId, pattern) ||
                EF.Functions.ILike(ban.Ip, pattern) ||
                EF.Functions.ILike(ban.Reason, pattern));
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        query = (sortBy?.Trim().ToLowerInvariant(), sortDescending) switch
        {
            ("nickname", false) => query.OrderBy(ban => ban.Nickname).ThenBy(ban => ban.BanId),
            ("nickname", true) => query.OrderByDescending(ban => ban.Nickname).ThenBy(ban => ban.BanId),
            ("reason", false) => query.OrderBy(ban => ban.Reason).ThenBy(ban => ban.BanId),
            ("reason", true) => query.OrderByDescending(ban => ban.Reason).ThenBy(ban => ban.BanId),
            ("created", false) => query.OrderBy(ban => ban.Created).ThenBy(ban => ban.BanId),
            _ => query.OrderByDescending(ban => ban.Created).ThenByDescending(ban => ban.BanId)
        };
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        return (items, total);
    }

    public async Task AddTaskAsync(ServerTask task, CancellationToken ct = default)
    {
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
