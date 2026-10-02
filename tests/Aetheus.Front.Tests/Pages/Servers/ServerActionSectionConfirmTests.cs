// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Servers.ServerDetailSections;
using Aetheus.Front.Tests.TestDoubles;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// The confirmation the server sections share (<see cref="ServerActionSectionBase"/>) is the
/// application's confirmation dialog: it names its verb, a destructive one is red, "Revenir" dismisses
/// it, and the action runs only on a yes. Proved through the cron section's Delete button.
/// </summary>
public sealed class ServerActionSectionConfirmTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerActionSectionConfirmTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        BunitTestHelper.UseImmediateDialogs(this);
        _handler.SetResponse(HttpMethod.Delete, "api/servers/40/cron", System.Net.HttpStatusCode.NoContent);
    }

    private ImmediateDialogService Confirmation => (ImmediateDialogService)Services.GetRequiredService<OmniDialogService>();

    private IRenderedComponent<ServerCronSection> RenderSection() =>
        Render<ServerCronSection>(parameters => parameters
            .Add(section => section.ServerId, 40)
            .Add(section => section.Server, new ServerDetailDto
            {
                Id = 40,
                Name = "cron-srv",
                Hostname = "10.0.0.4",
                Type = ServerType.Normal,
                Status = ServerStatus.Online,
                Tags = [],
                Services = [],
                Docker = new DockerDataDto { Containers = [], Images = [], ComposeStacks = [], Networks = [], Volumes = [] },
                Cron = new CronDataDto
                {
                    Jobs = [new CronJobDto { Id = "j1", User = "root", Schedule = "0 * * * *", Command = "/usr/bin/backup.sh" }]
                }
            }));

    private static AngleSharp.Dom.IElement DeleteButton(IRenderedComponent<ServerCronSection> cut) =>
        cut.FindAll("button.omni-button--danger").Single();

    [Fact]
    public void ADestructiveAction_AsksTheApplicationConfirmation_RedAndNamingItsVerb()
    {
        var cut = RenderSection();

        DeleteButton(cut).Click();

        Assert.Equal(1, Confirmation.OpenCount);
        var options = Confirmation.LastConfirmOptions!;
        Assert.True(options.Destructive);
        Assert.False(string.IsNullOrWhiteSpace(options.OkButtonText));
        Assert.NotEqual("Validate", options.OkButtonText);
        Assert.Equal("GoBack", options.CancelButtonText);
        Assert.DoesNotContain("dialog-overlay", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclinedConfirmation_RunsNothing()
    {
        var cut = RenderSection();

        DeleteButton(cut).Click();

        Assert.DoesNotContain(_handler.Requests, request => request.Method == "DELETE");
    }

    [Fact]
    public void AnAcceptedConfirmation_RunsTheAction()
    {
        var cut = RenderSection();
        Confirmation.ConfirmResult = true;

        DeleteButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains(_handler.Requests, request =>
            request.Method == "DELETE" && request.Url.Contains("api/servers/40/cron", StringComparison.Ordinal)));
    }
}
