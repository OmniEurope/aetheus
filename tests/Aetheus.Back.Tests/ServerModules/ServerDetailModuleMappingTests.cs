// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Aetheus.Back.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Aetheus.Back.Tests;

/// <summary>
/// Guards the ServerDetailDto mapping from entity state. The fake-server sub-menu on the
/// front decides which module links to show based on each <c>*.IsInstalled</c> flag - if
/// the mapper regresses (e.g. forgets to check MailState or CertbotCertificates), modules
/// will silently disappear from the UI. These tests lock in the mapping contract.
/// </summary>
public class ServerDetailModuleMappingTests
{
    private readonly IServerRepository _repoMock = Substitute.For<IServerRepository>();
    private readonly ServerService _sut;

    public ServerDetailModuleMappingTests()
    {
        var hubMock = Substitute.For<IHubContext<ServerHub>>();
        var alertHubMock = Substitute.For<IHubContext<AlertHub>>();
        var clientsMock = Substitute.For<IHubClients>();
        var groupMock = Substitute.For<IClientProxy>();
        clientsMock.Group(Arg.Any<string>()).Returns(groupMock);
        clientsMock.Groups(Arg.Any<IReadOnlyList<string>>()).Returns(groupMock);
        clientsMock.All.Returns(groupMock);
        hubMock.Clients.Returns(clientsMock);
        alertHubMock.Clients.Returns(clientsMock);

        _sut = new ServerService(_repoMock, Substitute.For<IServerHeartbeatRepository>(), hubMock, alertHubMock, Substitute.For<IAuditService>(),
            Substitute.For<Aetheus.Back.Components.Tasks.ITaskService>(),
            Options.Create(new BackgroundServicesOptions()), TimeProvider.System, Substitute.For<IDbTransactionScope>());
    }

    private void SetupServer(Server server)
    {
        _repoMock.GetServerDetailAsync(server.Id, Arg.Any<CancellationToken>())
            .Returns(server);
    }

    [Fact]
    public async Task GetServerDetailAsync_WithApacheState_IsInstalledTrue()
    {
        var server = BaseServer();
        server.ApacheState = new ApacheState { ServerId = 1, Version = "2.4", IsRunning = true };
        SetupServer(server);

        var dto = await _sut.GetServerDetailAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(dto);
        Assert.True(dto!.Apache.IsInstalled);
        Assert.True(dto.Apache.IsRunning);
        Assert.Equal("2.4", dto.Apache.Version);
    }

    [Fact]
    public async Task GetServerDetailAsync_WithoutApacheState_IsInstalledFalse()
    {
        var server = BaseServer();
        SetupServer(server);

        var dto = await _sut.GetServerDetailAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.False(dto!.Apache.IsInstalled);
    }

    [Fact]
    public async Task GetServerDetailAsync_WithCertbotCertificates_IsInstalledTrue()
    {
        var server = BaseServer();
        server.CertbotCertificates.Add(new CertbotCertificate
        {
            ServerId = 1,
            Name = "example.com",
            Domains = "[\"example.com\"]",
            ExpiryDate = DateTime.UtcNow.AddDays(60)
        });
        SetupServer(server);

        var dto = await _sut.GetServerDetailAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.True(dto!.Certbot.IsInstalled);
        Assert.Single(dto.Certbot.Certificates);
        Assert.Contains("example.com", dto.Certbot.Certificates[0].Domains);
    }

    [Fact]
    public async Task GetServerDetailAsync_WithMailState_IsInstalledTrue()
    {
        var server = BaseServer();
        server.MailState = new MailState
        {
            ServerId = 1,
            PostfixVersion = "3.7",
            DovecotVersion = "2.3",
            IsPostfixRunning = true,
            IsDovecotRunning = true
        };
        SetupServer(server);

        var dto = await _sut.GetServerDetailAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.True(dto!.Mail.IsInstalled);
        Assert.True(dto.Mail.IsPostfixRunning);
        Assert.Equal("3.7", dto.Mail.PostfixVersion);
    }

    [Fact]
    public async Task GetServerDetailAsync_WithTeamspeakState_MapsScalarStateWithoutInventory()
    {
        var server = BaseServer();
        server.TeamspeakState = new TeamspeakState
        {
            ServerId = 1,
            Version = "3.13",
            Platform = "Linux",
            IsRunning = true,
            ServerName = "Voice",
            VoicePort = 9987,
            QueryPort = 10011,
            OnlineClients = 5,
            MaxClients = 32,
            ChannelCount = 3
        };
        server.TeamspeakChannels.Add(new TeamspeakChannel
        {
            ServerId = 1,
            ChannelId = 1,
            Name = "Lobby"
        });
        SetupServer(server);

        var dto = await _sut.GetServerDetailAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.True(dto!.Teamspeak.IsInstalled);
        Assert.Equal(9987, dto.Teamspeak.VoicePort);
        Assert.Equal("Linux", dto.Teamspeak.Platform);
        Assert.Empty(dto.Teamspeak.Channels);
    }

    [Fact]
    public async Task GetServerDetailAsync_WithPortsentryState_IsInstalledTrue()
    {
        var server = BaseServer();
        server.PortsentryState = new PortsentryState
        {
            ServerId = 1,
            IsRunning = true,
            Version = "1.2",
            Mode = "atcp",
            BlockedCount = 2
        };
        server.PortsentryBlockedIps.Add(new PortsentryBlockedIp
        {
            ServerId = 1,
            IpAddress = "203.0.113.1",
            Protocol = "tcp",
            BlockedAt = DateTime.UtcNow,
            Reason = "scan"
        });
        SetupServer(server);

        var dto = await _sut.GetServerDetailAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.True(dto!.Portsentry.IsInstalled);
        Assert.True(dto.Portsentry.IsRunning);
        Assert.Empty(dto.Portsentry.BlockedIps);
    }

    [Fact]
    public async Task GetServerDetailAsync_WithRkhunterState_IsInstalledTrue()
    {
        var server = BaseServer();
        server.RkhunterState = new RkhunterState
        {
            ServerId = 1,
            Version = "1.4.6",
            DatabaseVersion = "2025-03",
            LastScanTime = DateTime.UtcNow.AddHours(-1),
            LastScanStatus = "completed",
            WarningCount = 1,
            ScanScheduleCron = "0 2 * * *"
        };
        SetupServer(server);

        var dto = await _sut.GetServerDetailAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.True(dto!.Rkhunter.IsInstalled);
        Assert.Equal("1.4.6", dto.Rkhunter.Version);
        Assert.Equal(1, dto.Rkhunter.WarningCount);
    }

    [Fact]
    public async Task GetServerDetailAsync_EmptyServer_AllModulesIsInstalledFalse()
    {
        var server = BaseServer();
        SetupServer(server);

        var dto = await _sut.GetServerDetailAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(dto);
        Assert.False(dto!.Apache.IsInstalled);
        Assert.False(dto.Certbot.IsInstalled);
        Assert.False(dto.Mail.IsInstalled);
        Assert.False(dto.Teamspeak.IsInstalled);
        Assert.False(dto.Portsentry.IsInstalled);
        Assert.False(dto.Rkhunter.IsInstalled);
    }

    [Fact]
    public async Task GetServerDetailAsync_WithDockerData_IsPopulated()
    {
        var server = BaseServer();
        server.DockerContainers.Add(new DockerContainer
        {
            ServerId = 1,
            ContainerId = "abc",
            Name = "web",
            Image = "nginx",
            State = "running",
            Status = "Up",
            Ports = "80:80"
        });
        SetupServer(server);

        var dto = await _sut.GetServerDetailAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Single(dto!.Docker.Containers);
        Assert.Equal("web", dto.Docker.Containers[0].Name);
    }

    private static Server BaseServer() => new()
    {
        Id = 1,
        Name = "test",
        Hostname = "test.local",
        Status = Aetheus.Shared.Components.Servers.ServerStatus.Online,
        Tags = "[]",
        CreatedAt = DateTime.UtcNow
    };
}
