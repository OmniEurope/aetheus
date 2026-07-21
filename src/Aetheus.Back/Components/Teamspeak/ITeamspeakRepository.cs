// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Teamspeak;

public interface ITeamspeakRepository
{
    Task<TeamspeakState?> GetStateAsync(int serverId, CancellationToken ct = default);
    Task<(List<TeamspeakChannel> Items, int Total)> GetChannelsPagedAsync(
        int serverId, string? search, int page, int pageSize, string? sortBy, bool sortDescending,
        CancellationToken ct = default);
    Task<(List<TeamspeakClient> Items, int Total)> GetClientsPagedAsync(
        int serverId, string? search, int page, int pageSize, string? sortBy, bool sortDescending,
        CancellationToken ct = default);
    Task<(List<TeamspeakBan> Items, int Total)> GetBansPagedAsync(
        int serverId, string? search, int page, int pageSize, string? sortBy, bool sortDescending,
        CancellationToken ct = default);
    Task AddTaskAsync(ServerTask task, CancellationToken ct = default);
}
