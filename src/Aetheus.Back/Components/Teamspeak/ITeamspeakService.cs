// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Teamspeak;

public interface ITeamspeakService
{
    Task<TeamspeakDataDto> GetStateAsync(int serverId, CancellationToken ct = default);
    Task<TeamspeakFilterValuesDto> GetFilterValuesAsync(int serverId, CancellationToken ct = default);
    Task<PaginatedResult<TeamspeakChannelDto>> GetChannelsAsync(
        int serverId, PaginationRequest request, CancellationToken ct = default);
    Task<PaginatedResult<TeamspeakClientDto>> GetClientsAsync(
        int serverId, PaginationRequest request, CancellationToken ct = default);
    Task<PaginatedResult<TeamspeakBanDto>> GetBansAsync(
        int serverId, PaginationRequest request, CancellationToken ct = default);
    Task ExecuteActionAsync(int serverId, TeamspeakActionRequest request, CancellationToken ct = default);
    Task SetupAsync(int serverId, TeamspeakSetupRequest request, CancellationToken ct = default);
    Task GetLogsAsync(int serverId, TeamspeakLogRequest request, CancellationToken ct = default);
    Task KickClientAsync(int serverId, TeamspeakKickRequest request, CancellationToken ct = default);
    Task BanClientAsync(int serverId, TeamspeakBanRequest request, CancellationToken ct = default);

    /// <summary>Item #9 tier-1 - relocate a connected client to another channel.</summary>
    Task MoveClientAsync(int serverId, TeamspeakMoveClientRequest request, CancellationToken ct = default);

    /// <summary>Item #9 tier-1 - popup a single client (vs server-wide message).</summary>
    Task PokeClientAsync(int serverId, TeamspeakPokeClientRequest request, CancellationToken ct = default);

    // ===== Tier 2/3 =====

    /// <summary>Detailed client info via TS3 <c>clientinfo clid=X</c> (IP, version, idle, etc.).</summary>
    Task GetClientInfoAsync(int serverId, TeamspeakClientInfoRequest request, CancellationToken ct = default);

    /// <summary>Compose warning + delay + restart in a single shell chain.</summary>
    Task GracefulRestartAsync(int serverId, TeamspeakGracefulRestartRequest request, CancellationToken ct = default);

    /// <summary>Capture a server snapshot (returned in the task output).</summary>
    Task CreateSnapshotAsync(int serverId, TeamspeakSnapshotCreateRequest request, CancellationToken ct = default);

    /// <summary>Restore a server snapshot. Confirmation must match the server name.</summary>
    Task DeploySnapshotAsync(int serverId, TeamspeakSnapshotDeployRequest request, CancellationToken ct = default);

    /// <summary>List the existing server groups (output in the task log).</summary>
    Task ListServerGroupsAsync(int serverId, CancellationToken ct = default);

    /// <summary>Promote/demote a client by adding it to a server group.</summary>
    Task AddClientToServerGroupAsync(int serverId, TeamspeakServerGroupAddRequest request, CancellationToken ct = default);

    /// <summary>Remove a client from a server group.</summary>
    Task RemoveClientFromServerGroupAsync(int serverId, TeamspeakServerGroupRemoveRequest request, CancellationToken ct = default);

    /// <summary>List existing TS3 admin/group tokens.</summary>
    Task ListTokensAsync(int serverId, CancellationToken ct = default);

    /// <summary>Mint a new TS3 admin/group token.</summary>
    Task CreateTokenAsync(int serverId, TeamspeakTokenCreateRequest request, CancellationToken ct = default);

    /// <summary>Delete an existing token.</summary>
    Task DeleteTokenAsync(int serverId, TeamspeakTokenDeleteRequest request, CancellationToken ct = default);

    /// <summary>Get virtualserver stats (slots used, transfer, uptime, …).</summary>
    Task GetServerInfoAsync(int serverId, CancellationToken ct = default);

    /// <summary>List all complaints for moderator review.</summary>
    Task ListComplaintsAsync(int serverId, CancellationToken ct = default);

    /// <summary>Dismiss a complaint (specific source or all for a target).</summary>
    Task DeleteComplaintAsync(int serverId, TeamspeakComplaintDeleteRequest request, CancellationToken ct = default);
    Task UnbanAsync(int serverId, int banId, CancellationToken ct = default);
    Task CreateChannelAsync(int serverId, TeamspeakCreateChannelRequest request, CancellationToken ct = default);
    Task EditChannelAsync(int serverId, TeamspeakEditChannelRequest request, CancellationToken ct = default);
    Task DeleteChannelAsync(int serverId, int channelId, CancellationToken ct = default);
    Task EditServerAsync(int serverId, TeamspeakServerEditRequest request, CancellationToken ct = default);
    Task SendGlobalMessageAsync(int serverId, TeamspeakGlobalMessageRequest request, CancellationToken ct = default);
}
