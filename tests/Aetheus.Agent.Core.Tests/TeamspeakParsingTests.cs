// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Agent.Core.Collectors;
using Aetheus.Agent.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public class TeamspeakParsingTests
{
    private static TeamspeakDataDto InvokeParse(string raw, int queryPort = 10011)
    {
        var sut = new TeamspeakCollector(NullLogger<TeamspeakCollector>.Instance, Substitute.For<IShellRunner>(), Substitute.For<ITeamspeakQueryClient>());
        var method = typeof(TeamspeakCollector).GetMethod("ParseQueryResult",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (TeamspeakDataDto)method.Invoke(sut, [raw, queryPort])!;
    }

    [Fact]
    public void ParseQueryResult_EmptyInput_ReturnsDefaults()
    {
        var dto = InvokeParse(string.Empty);
        Assert.True(dto.IsInstalled);
        Assert.True(dto.IsRunning);
        Assert.Empty(dto.Clients);
        Assert.Empty(dto.Channels);
    }

    [Fact]
    public void ParseQueryResult_ServerInfo_ExtractsFields()
    {
        var raw =
            "virtualserver_name=My\\sServer virtualserver_version=3.13.7 virtualserver_platform=Linux " +
            "virtualserver_uptime=123456 virtualserver_maxclients=64 virtualserver_port=9987\n" +
            "error id=0 msg=ok\n";
        var dto = InvokeParse(raw);

        Assert.Equal("My Server", dto.ServerName);
        Assert.Equal("3.13.7", dto.Version);
        Assert.Equal("Linux", dto.Platform);
        Assert.Equal(123456, dto.UptimeSeconds);
        Assert.Equal(64, dto.MaxClients);
        Assert.Equal(9987, dto.VoicePort);
    }

    [Fact]
    public void ParseQueryResult_NoServerInfo_DoesNotFabricateDefaultLimits()
    {
        // No serverinfo line: MaxClients / VoicePort must be 0 ("not collected"), never the TeamSpeak
        // defaults 32 / 9987 that would masquerade as measured values (audit F-ENG-07).
        var dto = InvokeParse("error id=0 msg=ok\n");

        Assert.Equal(0, dto.MaxClients);
        Assert.Equal(0, dto.VoicePort);
    }

    [Fact]
    public void ParseQueryResult_ClientList_ExtractsClients()
    {
        var raw =
            "clid=1 cid=1 client_unique_identifier=abc= client_nickname=Alice client_type=0|" +
            "clid=2 cid=1 client_unique_identifier=serveradmin client_nickname=ServerQuery client_type=1\n";
        var dto = InvokeParse(raw);

        Assert.Equal(2, dto.Clients.Count);
        Assert.Equal(1, dto.OnlineClients); // one is ServerQuery
        Assert.Contains(dto.Clients, c => c.Nickname == "Alice" && !c.IsServerQuery);
        Assert.Contains(dto.Clients, c => c.Nickname == "ServerQuery" && c.IsServerQuery);
    }

    [Fact]
    public void ParseQueryResult_ChannelList_ExtractsChannels()
    {
        var raw =
            "cid=1 pid=0 channel_order=0 total_clients=2 channel_maxclients=-1 " +
            "channel_flag_default=1 channel_flag_password=0 channel_flag_permanent=1 channel_name=Lobby|" +
            "cid=2 pid=1 channel_order=1 total_clients=0 channel_maxclients=10 " +
            "channel_flag_default=0 channel_flag_password=1 channel_flag_permanent=0 channel_name=Private\n";
        var dto = InvokeParse(raw);

        Assert.Equal(2, dto.ChannelCount);
        var lobby = dto.Channels.FirstOrDefault(c => c.Name == "Lobby");
        Assert.NotNull(lobby);
        Assert.True(lobby.IsDefault);
        Assert.True(lobby.IsPermanent);
        Assert.False(lobby.HasPassword);

        var priv = dto.Channels.FirstOrDefault(c => c.Name == "Private");
        Assert.NotNull(priv);
        Assert.True(priv.HasPassword);
        Assert.Equal(10, priv.MaxClients);
    }

    [Fact]
    public void ParseQueryResult_BanList_ExtractsBans()
    {
        var raw =
            "banid=7 ip=192.0.2.4 uid=uid123 lastnickname=Bad\\sActor created=1710000000 " +
            "duration=3600 reason=Repeated\\sspam\n";

        var dto = InvokeParse(raw);

        var ban = Assert.Single(dto.Bans);
        Assert.Equal(7, ban.BanId);
        Assert.Equal("192.0.2.4", ban.Ip);
        Assert.Equal("Bad Actor", ban.Nickname);
        Assert.Equal("Repeated spam", ban.Reason);
        Assert.Equal(3600, ban.Duration);
    }

    [Fact]
    public async Task RunServerQueryAsync_RequestsClientsChannelsAndBans()
    {
        var queryClient = Substitute.For<ITeamspeakQueryClient>();
        queryClient.ExecuteAsync(10011, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("error id=0 msg=ok");
        var sut = new TeamspeakCollector(
            NullLogger<TeamspeakCollector>.Instance,
            Substitute.For<IShellRunner>(),
            queryClient);
        var method = typeof(TeamspeakCollector).GetMethod(
            "RunServerQueryAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        await (Task<string?>)method.Invoke(sut, [10011, "secret", TestContext.Current.CancellationToken])!;

        await queryClient.Received(1).ExecuteAsync(
            10011,
            Arg.Is<string>(commands =>
                commands.Contains("clientlist -uid -times -info", StringComparison.Ordinal) &&
                commands.Contains("channellist", StringComparison.Ordinal) &&
                commands.Contains("banlist", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ParseQueryResult_QueryPort_PropagatedToDto()
    {
        var dto = InvokeParse("", queryPort: 12345);
        Assert.Equal(12345, dto.QueryPort);
    }
}
