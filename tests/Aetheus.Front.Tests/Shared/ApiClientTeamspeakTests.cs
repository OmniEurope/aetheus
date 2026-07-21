// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;

namespace Aetheus.Front.Tests;

/// <summary>Covers all ApiClient.Teamspeak methods (59 uncovered lines).</summary>
public class ApiClientTeamspeakTests
{
    private readonly BunitTestHelper.TestHandler _handler = new();
    private readonly ApiClient _api;

    public ApiClientTeamspeakTests()
    {
        var http = new HttpClient(_handler) { BaseAddress = new Uri("http://test/") };
        _api = new ApiClient(http);
        _handler.SetJsonResponse("api/servers/1/teamspeak", new TeamspeakDataDto { IsInstalled = true, IsRunning = true, Version = "3.13.7", OnlineClients = 4 });
        _handler.SetJsonResponse("api/servers/1/teamspeak/action", "{}");
        _handler.SetJsonResponse("api/servers/1/teamspeak/setup", "{}");
        _handler.SetJsonResponse("api/servers/1/teamspeak/logs", "{}");
        _handler.SetJsonResponse("api/servers/1/teamspeak/kick", "{}");
        _handler.SetJsonResponse("api/servers/1/teamspeak/ban", "{}");
        _handler.SetJsonResponse("api/servers/1/teamspeak/move-client", "{}");
        _handler.SetJsonResponse("api/servers/1/teamspeak/poke", "{}");
        _handler.SetPaginatedJsonResponse("api/servers/1/teamspeak/clients",
            new[] { new TeamspeakClientDto { ClientId = 1, Nickname = "Alice" } });
        _handler.SetPaginatedJsonResponse("api/servers/1/teamspeak/bans",
            new[] { new TeamspeakBanDto { BanId = 2, Nickname = "Blocked" } });
        _handler.SetPaginatedJsonResponse("api/servers/1/teamspeak/channels",
            new[] { new TeamspeakChannelDto { Id = 3, Name = "Lobby" } });
        _handler.SetJsonResponse("api/servers/1/teamspeak/server", "{}");
        _handler.SetJsonResponse("api/servers/1/teamspeak/message", "{}");
        _handler.SetJsonResponse("api/servers/1/teamspeak/clientinfo", "{}");
        _handler.SetJsonResponse("api/servers/1/teamspeak/graceful-restart", "{}");
        _handler.SetJsonResponse("api/servers/1/teamspeak/snapshots", "{}");
        _handler.SetJsonResponse("api/servers/1/teamspeak/snapshots/deploy", "{}");
        _handler.SetJsonResponse("api/servers/1/teamspeak/server-groups", "{}");
        _handler.SetJsonResponse("api/servers/1/teamspeak/server-groups/add", "{}");
        _handler.SetJsonResponse("api/servers/1/teamspeak/server-groups/remove", "{}");
        _handler.SetJsonResponse("api/servers/1/teamspeak/tokens", "{}");
        _handler.SetJsonResponse("api/servers/1/teamspeak/server-info", "{}");
        _handler.SetJsonResponse("api/servers/1/teamspeak/complaints", "{}");
    }

    [Fact]
    public async Task GetTeamspeakStateAsync_ReturnsData()
    { var r = await _api.GetTeamspeakStateAsync(1, Xunit.TestContext.Current.CancellationToken); Assert.True(r.IsRunning); Assert.Equal("3.13.7", r.Version); Assert.Equal(4, r.OnlineClients); }

    [Fact]
    public async Task ExecuteTeamspeakActionAsync_ReturnsStatus()
    { var r = await _api.ExecuteTeamspeakActionAsync(1, new TeamspeakActionRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task SetupTeamspeakAsync_ReturnsStatus()
    { var r = await _api.SetupTeamspeakAsync(1, new TeamspeakSetupRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task GetTeamspeakLogsAsync_ReturnsStatus()
    { var r = await _api.GetTeamspeakLogsAsync(1, new TeamspeakLogRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task KickTeamspeakClientAsync_ReturnsStatus()
    { var r = await _api.KickTeamspeakClientAsync(1, new TeamspeakKickRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task BanTeamspeakClientAsync_ReturnsStatus()
    { var r = await _api.BanTeamspeakClientAsync(1, new TeamspeakBanRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task MoveTeamspeakClientAsync_ReturnsStatus()
    { var r = await _api.MoveTeamspeakClientAsync(1, new TeamspeakMoveClientRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task PokeTeamspeakClientAsync_ReturnsStatus()
    { var r = await _api.PokeTeamspeakClientAsync(1, new TeamspeakPokeClientRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task GetTeamspeakBansAsync_ReturnsPage()
    { var r = await _api.GetTeamspeakBansAsync(1, 1, 25, ct: Xunit.TestContext.Current.CancellationToken); Assert.Equal(2, Assert.Single(r.Items).BanId); }

    [Fact]
    public async Task GetTeamspeakClientsAsync_ReturnsPage()
    { var r = await _api.GetTeamspeakClientsAsync(1, 1, 25, ct: Xunit.TestContext.Current.CancellationToken); Assert.Equal("Alice", Assert.Single(r.Items).Nickname); }

    [Fact]
    public async Task GetTeamspeakChannelsAsync_ReturnsPage()
    { var r = await _api.GetTeamspeakChannelsAsync(1, 1, 25, ct: Xunit.TestContext.Current.CancellationToken); Assert.Equal("Lobby", Assert.Single(r.Items).Name); }

    [Fact]
    public async Task UnbanTeamspeakClientAsync_ReturnsStatus()
    { var r = await _api.UnbanTeamspeakClientAsync(1, 42, Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task CreateTeamspeakChannelAsync_ReturnsStatus()
    { var r = await _api.CreateTeamspeakChannelAsync(1, new TeamspeakCreateChannelRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task EditTeamspeakChannelAsync_ReturnsStatus()
    { var r = await _api.EditTeamspeakChannelAsync(1, 10, new TeamspeakEditChannelRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task DeleteTeamspeakChannelAsync_ReturnsStatus()
    { var r = await _api.DeleteTeamspeakChannelAsync(1, 10, Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task EditTeamspeakServerAsync_ReturnsStatus()
    { var r = await _api.EditTeamspeakServerAsync(1, new TeamspeakServerEditRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task SendTeamspeakGlobalMessageAsync_ReturnsStatus()
    { var r = await _api.SendTeamspeakGlobalMessageAsync(1, new TeamspeakGlobalMessageRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task GetTeamspeakClientInfoAsync_ReturnsStatus()
    { var r = await _api.GetTeamspeakClientInfoAsync(1, new TeamspeakClientInfoRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task TeamspeakGracefulRestartAsync_ReturnsStatus()
    { var r = await _api.TeamspeakGracefulRestartAsync(1, new TeamspeakGracefulRestartRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task CreateTeamspeakSnapshotAsync_ReturnsStatus()
    { var r = await _api.CreateTeamspeakSnapshotAsync(1, new TeamspeakSnapshotCreateRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task DeployTeamspeakSnapshotAsync_ReturnsStatus()
    { var r = await _api.DeployTeamspeakSnapshotAsync(1, new TeamspeakSnapshotDeployRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task ListTeamspeakServerGroupsAsync_ReturnsStatus()
    { var r = await _api.ListTeamspeakServerGroupsAsync(1, Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task AddTeamspeakServerGroupClientAsync_ReturnsStatus()
    { var r = await _api.AddTeamspeakServerGroupClientAsync(1, new TeamspeakServerGroupAddRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task RemoveTeamspeakServerGroupClientAsync_ReturnsStatus()
    { var r = await _api.RemoveTeamspeakServerGroupClientAsync(1, new TeamspeakServerGroupRemoveRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task ListTeamspeakTokensAsync_ReturnsStatus()
    { var r = await _api.ListTeamspeakTokensAsync(1, Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task CreateTeamspeakTokenAsync_ReturnsStatus()
    { var r = await _api.CreateTeamspeakTokenAsync(1, new TeamspeakTokenCreateRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task DeleteTeamspeakTokenAsync_ReturnsStatus()
    { var r = await _api.DeleteTeamspeakTokenAsync(1, new TeamspeakTokenDeleteRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task GetTeamspeakServerInfoAsync_ReturnsStatus()
    { var r = await _api.GetTeamspeakServerInfoAsync(1, Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task ListTeamspeakComplaintsAsync_ReturnsStatus()
    { var r = await _api.ListTeamspeakComplaintsAsync(1, Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }

    [Fact]
    public async Task DeleteTeamspeakComplaintAsync_ReturnsStatus()
    { var r = await _api.DeleteTeamspeakComplaintAsync(1, new TeamspeakComplaintDeleteRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success); }
}
