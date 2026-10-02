// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// Guards the non-obvious default values baked into the Teamspeak request/DTO records.
/// Plain "set property → read same property" round-trips were dropped (they tested the
/// compiler, not behavior); a change to one of these defaults IS a behavioral change
/// (port mapping, channel capacity, permanence) and is what's worth a regression guard.
/// </summary>
public class TeamspeakDtosTests
{
    [Fact]
    public void TeamspeakChannelDto_MaxClients_DefaultsToUnlimitedSentinel()
    {
        // -1 is the "unlimited" sentinel surfaced in the channel UI.
        Assert.Equal(-1, new TeamspeakChannelDto().MaxClients);
    }

    [Fact]
    public void TeamspeakSetupRequest_Defaults_MatchTs3Conventions()
    {
        var req = new TeamspeakSetupRequest();
        Assert.Equal("/opt/teamspeak3-server_linux_amd64", req.InstallPath);
        Assert.Equal(9987, req.VoicePort);  // TS3 default voice port
        Assert.Equal(10011, req.QueryPort); // TS3 default ServerQuery port
    }

    [Fact]
    public void TeamspeakCreateChannelRequest_IsPermanent_DefaultsTrue()
    {
        // New channels are permanent unless explicitly made temporary.
        Assert.True(new TeamspeakCreateChannelRequest().IsPermanent);
    }

    [Fact]
    public void TeamspeakGracefulRestartRequest_Defaults_AreSane()
    {
        var req = new TeamspeakGracefulRestartRequest();
        Assert.Equal(60, req.WarningSeconds);
        Assert.Contains("{0}", req.WarningMessage); // placeholder for the countdown
    }

    [Fact]
    public void TeamspeakLogRequest_Lines_DefaultsTo100()
    {
        Assert.Equal(100, new TeamspeakLogRequest().Lines);
    }
}
