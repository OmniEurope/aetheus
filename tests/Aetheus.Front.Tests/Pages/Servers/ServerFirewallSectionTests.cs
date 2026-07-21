// SPDX-License-Identifier: EUPL-1.2
using System.Net.Http;
using System.Reflection;
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Shared.DTOs;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerFirewallSectionTests : BunitContext
{
    private BunitTestHelper.TestHandler? _handler;

    private static ServerFirewallDto Firewall(bool active, bool cap) => new()
    {
        Installed = true,
        Active = active,
        StatusKnown = true,
        FirewallManagementAvailable = cap,
        Rules =
        [
            new FirewallRuleDto { Number = 1, Port = 22, Protocol = "tcp", Action = "allow", Source = "Anywhere", Raw = "22/tcp ALLOW Anywhere" },
            new FirewallRuleDto { Number = 2, Port = 8080, Protocol = "tcp", Action = "deny", Source = "10.0.0.0/8", Raw = "8080/tcp DENY 10.0.0.0/8" }
        ]
    };

    private IRenderedComponent<ServerFirewallSection> RenderWith(ServerFirewallDto dto, bool? cap)
    {
        _handler = BunitTestHelper.RegisterServices(this);
        _handler.SetJsonResponse(HttpMethod.Get, "firewall", dto);
        return Render<ServerFirewallSection>(p => p
            .Add(c => c.ServerId, 1)
            .Add(c => c.FirewallManagementAvailable, cap));
    }

    [Fact]
    public void ActiveWithCapability_RendersRulesAndActiveBadge()
    {
        var cut = RenderWith(Firewall(active: true, cap: true), cap: true);

        Assert.Contains("22", cut.Markup);
        Assert.Contains("8080", cut.Markup);
        Assert.Contains("FirewallActive", cut.Markup);
        Assert.Contains("lock_open", cut.Markup); // add-rule form present when capable
    }

    [Fact]
    public void WithoutCapability_ShowsManageDisabledHint_AndNoForm()
    {
        var cut = RenderWith(Firewall(active: true, cap: false), cap: false);

        Assert.Contains("FirewallManageDisabledHint", cut.Markup);
        Assert.DoesNotContain("lock_open", cut.Markup);
    }

    [Fact]
    public void NotInstalled_ShowsNotAvailable()
    {
        var cut = RenderWith(new ServerFirewallDto { Installed = false }, cap: null);

        Assert.Contains("FirewallNotInstalledTitle", cut.Markup);
    }

    [Fact]
    public void StatusUnknown_ShowsDiagnosticInsteadOfInactiveStateOrControls()
    {
        var cut = RenderWith(new ServerFirewallDto
        {
            Installed = true,
            StatusKnown = false,
            CollectionDiagnostics = "ufw status could not be read",
            FirewallManagementAvailable = true
        }, cap: true);

        Assert.Contains("FirewallStatusUnknown", cut.Markup);
        Assert.Contains("ufw status could not be read", cut.Markup);
        Assert.DoesNotContain("FirewallInactive", cut.Markup);
        Assert.DoesNotContain("lock_open", cut.Markup);
    }

    [Fact]
    public void SameServerParentRerender_DoesNotReloadFirewall()
    {
        var cut = RenderWith(Firewall(active: true, cap: true), cap: true);

        cut.Render(parameters => parameters
            .Add(component => component.ServerId, 1)
            .Add(component => component.FirewallManagementAvailable, false));

        Assert.Single(_handler!.Requests, request =>
            request.Method == "GET" && request.Url.Contains("firewall"));
    }

    [Fact]
    public async Task DeleteRule_UnknownProtocol_DoesNotSendRequest()
    {
        var cut = RenderWith(Firewall(active: true, cap: true), cap: true);
        var method = typeof(ServerFirewallSection).GetMethod(
            "DeleteRuleAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance,
            [new FirewallRuleDto { Port = 8080, Protocol = "all", Source = "Anywhere", Raw = "8080 ALLOW Anywhere" }])!);

        Assert.DoesNotContain(_handler!.Requests, request => request.Method == "DELETE");
    }
}
