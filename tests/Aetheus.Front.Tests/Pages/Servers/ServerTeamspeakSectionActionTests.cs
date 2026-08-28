// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Front.Tests.TestDoubles;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// Drives the TeamSpeak section's toolbar. Each action touches a live voice server with connected
/// users, so it must reach the endpoint of the server on screen, and the ones that are destructive or
/// disruptive must go through a dialog rather than firing on a single click.
/// </summary>
public sealed class ServerTeamspeakSectionActionTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;
    private readonly ImmediateDialogService _dialog;

    public ServerTeamspeakSectionActionTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        BunitTestHelper.UseImmediateDialogs(this);
        _dialog = (ImmediateDialogService)Services.GetRequiredService<DialogService>();
    }

    private static ServerDetailDto Server(bool installed = true, bool running = true) => new()
    {
        Id = 5,
        Name = "ts-srv",
        Hostname = "10.0.0.5",
        Type = ServerType.Normal,
        Status = ServerStatus.Online,
        Tags = ["teamspeak"],
        Services = [],
        Teamspeak = new TeamspeakDataDto
        {
            IsInstalled = installed,
            IsRunning = running,
            Version = "3.13.7",
            Clients = [],
            Channels = [],
            Bans = []
        }
    };

    private IRenderedComponent<ServerTeamspeakSection> RenderSection(ServerDetailDto? server = null)
    {
        var s = server ?? Server();
        _handler.SetJsonResponse($"api/servers/{s.Id}/teamspeak", s.Teamspeak);
        _handler.SetPaginatedJsonResponse($"api/servers/{s.Id}/teamspeak/clients", Array.Empty<TeamspeakClientDto>());
        _handler.SetPaginatedJsonResponse($"api/servers/{s.Id}/teamspeak/channels", Array.Empty<TeamspeakChannelDto>());
        _handler.SetPaginatedJsonResponse($"api/servers/{s.Id}/teamspeak/bans", Array.Empty<TeamspeakBanDto>());
        return Render<ServerTeamspeakSection>(p => p
            .Add(x => x.Server, s)
            .Add(x => x.ServerId, s.Id));
    }

    private static IElement? TryButton(IRenderedComponent<ServerTeamspeakSection> cut, string label) =>
        cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains(label, StringComparison.Ordinal));

    [Fact]
    public void AnInstalledServerOffersItsManagementToolbar()
    {
        var cut = RenderSection(Server(installed: true));

        Assert.NotNull(TryButton(cut, "EditServer"));
        Assert.NotNull(TryButton(cut, "SendMessage"));
        Assert.NotNull(TryButton(cut, "GracefulRestart"));
        Assert.NotNull(TryButton(cut, "SnapshotCreate"));
    }

    [Fact]
    public void AnUninstalledServerOffersSetupAndNotTheManagementToolbar()
    {
        // Editing a virtual server that does not exist can only fail at the agent.
        var cut = RenderSection(Server(installed: false, running: false));

        Assert.NotNull(TryButton(cut, "TeamspeakSetup"));
        Assert.Null(TryButton(cut, "EditServer"));
        Assert.Null(TryButton(cut, "GracefulRestart"));
    }

    [Fact]
    public void TheQuickConnectLinkPointsAtTheServersOwnHost()
    {
        // A wrong host here sends the operator's TeamSpeak client to somebody else's server.
        var cut = RenderSection();

        var link = cut.FindAll("button").First(b => b.TextContent.Contains("QuickConnect", StringComparison.Ordinal));
        Assert.Contains("10.0.0.5", link.GetAttribute("title") ?? string.Empty, StringComparison.Ordinal);
        Assert.StartsWith("ts3server://", link.GetAttribute("title") ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSectionLoadsItsOwnServersTeamspeakData()
    {
        // Loading another server id here would show someone else's voice server. The per-tab grids
        // (clients, channels, bans) load lazily when their tab is opened, so only the section's own
        // payload is fetched on render.
        var cut = RenderSection();

        cut.WaitForAssertion(
            () => Assert.Contains(_handler.Requests, r =>
                r.Url.Contains("servers/5/teamspeak", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(3));
        Assert.DoesNotContain(_handler.Requests, r =>
            r.Url.Contains("servers/6/", StringComparison.Ordinal));
    }

    [Fact]
    public void AGracefulRestartAsksBeforeDisconnectingAnyone()
    {
        // It drops every connected client; firing it on one click would be a trap.
        _dialog.OpenResult = null;
        var cut = RenderSection();
        // Count writes only: a late GET from the async load would otherwise fail this for the wrong reason.
        var before = _handler.Requests.Count(r => r.Method == "POST");

        TryButton(cut, "GracefulRestart")!.Click();

        cut.WaitForAssertion(() => Assert.True(_dialog.OpenCount > 0), TimeSpan.FromSeconds(3));
        Assert.Equal(before, _handler.Requests.Count(r => r.Method == "POST"));
    }

    [Fact]
    public void SendingAMessageGoesThroughADialogRatherThanFiringImmediately()
    {
        // A broadcast reaches every connected user; there is no undo.
        _dialog.OpenResult = null;
        var cut = RenderSection();
        // Count writes only: a late GET from the async load would otherwise fail this for the wrong reason.
        var before = _handler.Requests.Count(r => r.Method == "POST");

        TryButton(cut, "SendMessage")!.Click();

        cut.WaitForAssertion(() => Assert.True(_dialog.OpenCount > 0), TimeSpan.FromSeconds(3));
        Assert.Equal(before, _handler.Requests.Count(r => r.Method == "POST"));
    }

    [Fact]
    public void EditingTheVirtualServerOpensADialogAndCallsNothingUntilConfirmed()
    {
        _dialog.OpenResult = null;
        var cut = RenderSection();
        // Count writes only: a late GET from the async load would otherwise fail this for the wrong reason.
        var before = _handler.Requests.Count(r => r.Method == "POST");

        TryButton(cut, "EditServer")!.Click();

        cut.WaitForAssertion(() => Assert.True(_dialog.OpenCount > 0), TimeSpan.FromSeconds(3));
        Assert.Equal(before, _handler.Requests.Count(r => r.Method == "POST"));
    }

    [Fact]
    public void DeployingASnapshotIsGatedByADialog()
    {
        // Deploying a snapshot overwrites the live virtual server's configuration.
        _dialog.OpenResult = null;
        var cut = RenderSection();
        // Count writes only: a late GET from the async load would otherwise fail this for the wrong reason.
        var before = _handler.Requests.Count(r => r.Method == "POST");

        TryButton(cut, "SnapshotDeploy")!.Click();

        cut.WaitForAssertion(() => Assert.True(_dialog.OpenCount > 0), TimeSpan.FromSeconds(3));
        Assert.Equal(before, _handler.Requests.Count(r => r.Method == "POST"));
    }

    // --- confirmed dialogs: the handler continues past the dialog and calls the API ---------------

    private TeamspeakDialogModel Confirmed(TeamspeakDialogMode mode) => new()
    {
        Mode = mode,
        Message = "maintenance in 5 minutes",
        WarningSeconds = 30,
        SnapshotBlob = "snapshot-payload",
        Confirmation = "ts-srv",
        ExpectedConfirmation = "ts-srv"
    };

    [Fact]
    public void ConfirmingAGracefulRestart_PostsToTheGracefulRestartEndpoint()
    {
        _handler.SetJsonResponse(
            HttpMethod.Post, "api/servers/5/teamspeak/graceful-restart", new { success = true });
        _dialog.OpenResult = Confirmed(TeamspeakDialogMode.GracefulRestart);
        var cut = RenderSection();

        TryButton(cut, "GracefulRestart")!.Click();

        cut.WaitForAssertion(
            () => Assert.Contains(_handler.Requests, r =>
                r.Method == "POST" && r.Url.Contains("teamspeak/graceful-restart", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void ConfirmingABroadcast_PostsToTheMessageEndpoint()
    {
        _handler.SetJsonResponse(HttpMethod.Post, "api/servers/5/teamspeak/message", new { success = true });
        _dialog.OpenResult = Confirmed(TeamspeakDialogMode.Message);
        var cut = RenderSection();

        TryButton(cut, "SendMessage")!.Click();

        cut.WaitForAssertion(
            () => Assert.Contains(_handler.Requests, r =>
                r.Method == "POST" && r.Url.Contains("teamspeak/message", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void ConfirmingASnapshotDeploy_PostsToTheSnapshotsEndpoint()
    {
        _handler.SetJsonResponse(HttpMethod.Post, "api/servers/5/teamspeak/snapshots", new { success = true });
        _dialog.OpenResult = Confirmed(TeamspeakDialogMode.SnapshotDeploy);
        var cut = RenderSection();

        TryButton(cut, "SnapshotDeploy")!.Click();

        cut.WaitForAssertion(
            () => Assert.Contains(_handler.Requests, r =>
                r.Method == "POST" && r.Url.Contains("teamspeak/snapshots", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void EachConfirmedActionTargetsItsOwnEndpoint_NotASharedOne()
    {
        // A single generic endpoint would mean the dialog choice does not decide what happens.
        _handler.SetJsonResponse(
            HttpMethod.Post, "api/servers/5/teamspeak/graceful-restart", new { success = true });
        _handler.SetJsonResponse(HttpMethod.Post, "api/servers/5/teamspeak/message", new { success = true });

        _dialog.OpenResult = Confirmed(TeamspeakDialogMode.GracefulRestart);
        var restart = RenderSection();
        TryButton(restart, "GracefulRestart")!.Click();
        restart.WaitForAssertion(
            () => Assert.Contains(_handler.Requests, r =>
                r.Url.Contains("graceful-restart", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(3));

        _dialog.OpenResult = Confirmed(TeamspeakDialogMode.Message);
        var message = RenderSection();
        TryButton(message, "SendMessage")!.Click();
        message.WaitForAssertion(
            () => Assert.Contains(_handler.Requests, r =>
                r.Url.Contains("teamspeak/message", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void ConfirmingSetupOnAnUninstalledServer_PostsToTheSetupEndpoint()
    {
        _handler.SetJsonResponse(HttpMethod.Post, "api/servers/5/teamspeak/setup", new { success = true });
        _dialog.OpenResult = Confirmed(TeamspeakDialogMode.Setup);
        var cut = RenderSection(Server(installed: false, running: false));

        TryButton(cut, "TeamspeakSetup")!.Click();

        cut.WaitForAssertion(
            () => Assert.Contains(_handler.Requests, r =>
                r.Method == "POST" && r.Url.Contains("teamspeak/setup", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(3));
    }
}
