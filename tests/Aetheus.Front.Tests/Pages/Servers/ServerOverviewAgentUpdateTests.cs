// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Servers.ServerDetailSections;
using Aetheus.Front.Tests.TestDoubles;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// Recette R2-033: when the agent should be updated, the Overview offers "Update" right under its
/// version, to whoever may write on the server, and the click asks the same confirmation as the Tools
/// menu before queuing the update.
/// </summary>
public class ServerOverviewAgentUpdateTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerOverviewAgentUpdateTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        BunitTestHelper.UseImmediateDialogs(this);
        _handler.SetJsonResponse("api/servers/7/agent/update", new AgentUpdateResponse { ServerId = 7, RequestId = 1 });
    }

    private static ServerDetailDto Server(AgentCompatibilityStatus status, string installed = "1.0.1547",
        string target = "1.0.1552") => new()
        {
            Id = 7,
            Name = "web-prod",
            Hostname = "10.0.0.7",
            IpAddress = "10.0.0.7",
            Type = ServerType.Normal,
            Status = ServerStatus.Online,
            AgentVersion = installed,
            AgentCompatibility = new AgentCompatibilityDto { Status = status, InstalledVersion = installed, TargetVersion = target },
            Tags = [],
            Services = [],
            Docker = new DockerDataDto { Containers = [], Images = [], ComposeStacks = [], Networks = [], Volumes = [] },
            Apache = new ApacheDataDto(),
            Certbot = new CertbotDataDto(),
            Cron = new CronDataDto(),
            Mail = new MailDataDto(),
            Teamspeak = new TeamspeakDataDto(),
            Portsentry = new PortsentryDataDto(),
            Rkhunter = new RkhunterDataDto()
        };

    private IRenderedComponent<ServerOverviewSection> RenderOverview(ServerDetailDto server) =>
        Render<ServerOverviewSection>(parameters => parameters.Add(section => section.Server, server));

    private static AngleSharp.Dom.IElement AgentVersionItem(IRenderedComponent<ServerOverviewSection> cut) =>
        cut.FindAll(".essential-item").Single(item => item.TextContent.Contains("AgentVersion", StringComparison.Ordinal));

    private static AngleSharp.Dom.IElement? UpdateButton(IRenderedComponent<ServerOverviewSection> cut) =>
        AgentVersionItem(cut).QuerySelectorAll("button").SingleOrDefault(button => button.TextContent.Contains("Update", StringComparison.Ordinal));

    private bool UpdateRequested => _handler.Requests.Any(request =>
        request.Method == "POST" && request.Url.Contains("api/servers/7/agent/update", StringComparison.Ordinal));

    [Theory]
    [InlineData(AgentCompatibilityStatus.UpdateRecommended)]
    [InlineData(AgentCompatibilityStatus.UpdateRequired)]
    public void AnUpdateDue_ShowsABlueUpdateButtonUnderTheVersion(AgentCompatibilityStatus status)
    {
        var cut = RenderOverview(Server(status));

        var button = UpdateButton(cut);
        Assert.NotNull(button);
        Assert.Contains("omni-button--primary", button.ClassList);
        Assert.Equal("UpdateAgent", button.GetAttribute("title"));
        Assert.NotNull(button.QuerySelector("svg"));
    }

    [Fact]
    public void AnAgentUpToDate_HasNoUpdateButton()
    {
        var cut = RenderOverview(Server(AgentCompatibilityStatus.UpToDate, installed: "1.0.1552"));

        Assert.Null(UpdateButton(cut));
    }

    [Fact]
    public void ATargetEqualToTheInstalledVersion_HasNoUpdateButton()
    {
        var cut = RenderOverview(Server(AgentCompatibilityStatus.UpdateRecommended, installed: "1.0.1552"));

        Assert.Null(UpdateButton(cut));
    }

    [Fact]
    public void AReader_IsNotOfferedTheUpdate()
    {
        Services.GetRequiredService<PermissionService>().SetPermissions([], isAdmin: false);

        var cut = RenderOverview(Server(AgentCompatibilityStatus.UpdateRequired));

        Assert.Null(UpdateButton(cut));
    }

    [Fact]
    public void TheClick_ConfirmsWithBothVersions_ThenQueuesTheUpdate()
    {
        var dialog = (ImmediateDialogService)Services.GetRequiredService<OmniDialogService>();
        dialog.ConfirmResult = true;
        var cut = RenderOverview(Server(AgentCompatibilityStatus.UpdateRequired));

        UpdateButton(cut)!.Click();

        cut.WaitForAssertion(() => Assert.True(UpdateRequested));
        Assert.Equal("UpdateAgent", dialog.LastTitle);
        Assert.Equal("Update", dialog.LastConfirmOptions!.OkButtonText);
        Assert.Equal("GoBack", dialog.LastConfirmOptions.CancelButtonText);
    }

    [Fact]
    public void ADeclinedConfirmation_QueuesNothing()
    {
        var dialog = (ImmediateDialogService)Services.GetRequiredService<OmniDialogService>();
        dialog.ConfirmResult = false;
        var cut = RenderOverview(Server(AgentCompatibilityStatus.UpdateRequired));

        UpdateButton(cut)!.Click();

        Assert.Equal(1, dialog.OpenCount);
        Assert.False(UpdateRequested);
    }
}
