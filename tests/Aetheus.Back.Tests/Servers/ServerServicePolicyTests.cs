// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Aetheus.Back.Services;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Aetheus.Back.Tests.Servers;

public class ServerServicePolicyTests
{
    private readonly IServerRepository _repo = Substitute.For<IServerRepository>();
    private readonly IServerHeartbeatRepository _heartbeatRepoMock = Substitute.For<IServerHeartbeatRepository>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly ServerService _sut;

    public ServerServicePolicyTests()
    {
        var hub = Substitute.For<IHubContext<ServerHub>>();
        var alertHub = Substitute.For<IHubContext<AlertHub>>();
        var clients = Substitute.For<IHubClients>();
        var proxy = Substitute.For<IClientProxy>();
        clients.All.Returns(proxy);
        clients.Group(Arg.Any<string>()).Returns(proxy);
        clients.Groups(Arg.Any<IReadOnlyList<string>>()).Returns(proxy);
        hub.Clients.Returns(clients);
        alertHub.Clients.Returns(clients);

        _sut = new ServerService(_repo, _heartbeatRepoMock, hub, alertHub, _audit,
            Substitute.For<Aetheus.Back.Components.Tasks.ITaskService>(),
            Options.Create(new BackgroundServicesOptions()), TimeProvider.System,
            Substitute.For<IDbTransactionScope>());
    }

    private static Server NewServer(bool runner = false, bool isolation = false) =>
        new()
        {
            Id = 1,
            Name = "web-01",
            Hostname = "web-01.local",
            Status = ServerStatus.Online,
            PipelineRunnerEnabled = runner,
            RequireContainerIsolation = isolation
        };

    [Fact]
    public async Task SetPipelineRunnerEnabled_ServerNotFound_ReturnsNull()
    {
        _repo.FindServerAsync(9, Arg.Any<CancellationToken>()).Returns((Server?)null);

        Assert.Null(await _sut.SetPipelineRunnerEnabledAsync(9, true, "admin", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetPipelineRunnerEnabled_Unchanged_ReturnsDtoWithoutAudit()
    {
        _repo.FindServerAsync(1, Arg.Any<CancellationToken>()).Returns(NewServer(runner: true));

        var result = await _sut.SetPipelineRunnerEnabledAsync(1, true, "admin", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        await _audit.DidNotReceive().LogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPipelineRunnerEnabled_Changed_SavesAndAudits()
    {
        var server = NewServer(runner: false);
        _repo.FindServerAsync(1, Arg.Any<CancellationToken>()).Returns(server);

        var result = await _sut.SetPipelineRunnerEnabledAsync(1, true, "admin", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(server.PipelineRunnerEnabled);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync("PipelineRunnerEnabled", "Server", 1, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetContainerIsolationRequired_NotFound_ReturnsNull()
    {
        _repo.FindServerAsync(9, Arg.Any<CancellationToken>()).Returns((Server?)null);

        Assert.Null(await _sut.SetContainerIsolationRequiredAsync(9, true, "admin", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetContainerIsolationRequired_Unchanged_NoAudit()
    {
        _repo.FindServerAsync(1, Arg.Any<CancellationToken>()).Returns(NewServer(isolation: true));

        var result = await _sut.SetContainerIsolationRequiredAsync(1, true, "admin", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetContainerIsolationRequired_Changed_SavesAndAudits()
    {
        var server = NewServer(isolation: false);
        _repo.FindServerAsync(1, Arg.Any<CancellationToken>()).Returns(server);

        var result = await _sut.SetContainerIsolationRequiredAsync(1, true, "admin", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(server.RequireContainerIsolation);
        await _audit.Received(1).LogAsync("ContainerIsolationRequired", "Server", 1, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // S-TECH-HBS4: GetRecentHeartbeatsAsync was removed with the orphaned heartbeats endpoint
    // (the HeartbeatSparkline consumer is gone). The repo method GetRecentMetricTimestampsAsync keeps its
    // own dedicated coverage in ServerRepositoryMetricsTests.

    // --- DiagnoseAsync ---

    [Fact]
    public async Task Diagnose_ServerMissing_ReturnsNull()
    {
        _repo.FindServerWithTokensAsync(9, Arg.Any<CancellationToken>()).Returns((Server?)null);

        Assert.Null(await _sut.DiagnoseAsync(9, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Diagnose_ActiveToken_ReportsValidTokenAndHeartbeat()
    {
        var server = NewServer();
        server.LastHeartbeat = DateTime.UtcNow.AddMinutes(-2);
        server.AgentVersion = "1.2.3";
        server.Tokens = [new ServerToken { Id = 1, ServerId = 1, IsRevoked = false, CreatedAt = DateTime.UtcNow.AddDays(-1), ExpiresAt = DateTime.UtcNow.AddDays(30) }];
        _repo.FindServerWithTokensAsync(1, Arg.Any<CancellationToken>()).Returns(server);

        var result = await _sut.DiagnoseAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.TokenValid);
        Assert.NotNull(result.LastHeartbeatUtc);
        Assert.Equal("1.2.3", result.AgentVersion);
    }

    [Fact]
    public async Task Diagnose_NeverReportedWithRevokedToken_ReportsInvalidToken()
    {
        var server = NewServer();
        server.LastHeartbeat = DateTime.MinValue;
        server.Tokens = [new ServerToken { Id = 1, ServerId = 1, IsRevoked = true, CreatedAt = DateTime.UtcNow.AddDays(-10), ExpiresAt = DateTime.UtcNow.AddDays(-1) }];
        _repo.FindServerWithTokensAsync(1, Arg.Any<CancellationToken>()).Returns(server);

        var result = await _sut.DiagnoseAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(result.TokenValid);
        Assert.Null(result.LastHeartbeatUtc);
    }
}
