// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Layout;
using Aetheus.Front.Services;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;

namespace Aetheus.Front.Tests.Pages;

public class NavMenuTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public NavMenuTests()
    {
        // Register AlertNotificationService first - it's needed by NavMenu
        // We must create it before RegisterServices freezes the container
        var authProvider = new AuthStateProvider(Substitute.For<IJSRuntime>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthStateProvider>.Instance);
        typeof(AuthStateProvider).GetProperty("Token")!.SetValue(authProvider, "test-token");
        typeof(AuthStateProvider).GetProperty("Roles")!.SetValue(authProvider, new List<string> { "Admin" });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApiBaseUrl"] = "http://test:5301" })
            .Build();

        var hubFactory = new HubConnectionFactory(
            config,
            authProvider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthDelegatingHandler>.Instance);
        var alertService = new AlertNotificationService(authProvider, hubFactory);
        Services.AddSingleton(alertService);

        _handler = BunitTestHelper.RegisterServices(this, authenticated: true, isAdmin: true);
    }

    [Fact]
    public void Renders_MenuItems()
    {
        var cut = Render<NavMenu>();
        Assert.Contains("Dashboard", cut.Markup);
        Assert.Contains("Servers", cut.Markup);
        Assert.Contains("Projects", cut.Markup);
        Assert.Contains("Settings", cut.Markup);
        Assert.Contains("HelpCenter", cut.Markup);
    }

    [Fact]
    public void AdminUser_RendersAdminSection()
    {
        var cut = Render<NavMenu>();
        Assert.Contains("Administration", cut.Markup);
    }

    [Fact]
    public void Renders_HelpCenter()
    {
        var cut = Render<NavMenu>();
        Assert.Contains("HelpCenter", cut.Markup);
    }

    [Fact]
    public void Dispose_UnsubscribesEvents()
    {
        var cut = Render<NavMenu>();
        var projectNav = Services.GetRequiredService<ProjectNavContextService>();

        var instance = (IDisposable)cut.Instance;
        instance.Dispose();

        // After Dispose, NavMenu is unsubscribed from ProjectNav.OnChanged: firing it must NOT drive
        // another render (a leftover subscription would re-render a disposed component).
        var afterDispose = cut.RenderCount;
        projectNav.Set(99);
        Assert.Equal(afterDispose, cut.RenderCount);
    }

    // ──── Server sub-menu capabilities (fake server scenario) ────

    [Fact]
    public void ServerSubMenu_ShowsAllSectionLinks()
    {
        // On /servers/{id}: all section links visible (capabilities no longer gate the nav).
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("servers/7/overview");

        var cut = Render<NavMenu>();

        Assert.Contains("servers/7/overview", cut.Markup);
        Assert.Contains("servers/7/services", cut.Markup);
        Assert.Contains("servers/7/projects", cut.Markup);
        Assert.Contains("servers/7/pipelines", cut.Markup);
        Assert.Contains("servers/7/libraries", cut.Markup);
        Assert.Contains("servers/7/vaults", cut.Markup);
        Assert.Contains("servers/7/releases", cut.Markup);
        Assert.Contains("servers/7/tasks", cut.Markup);
        Assert.Contains("servers/7/logs", cut.Markup);
    }

    [Fact]
    public void ServerSubMenu_NoModuleSectionsInNav()
    {
        // Module sections (docker, apache, certbot…) are consolidated under
        // the Services tab - individual routes don't appear in the nav.
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("servers/3/overview");

        var cut = Render<NavMenu>();

        Assert.Contains("servers/3/services", cut.Markup);
        Assert.DoesNotContain("servers/3/apache", cut.Markup);
        Assert.DoesNotContain("servers/3/certbot", cut.Markup);
        Assert.DoesNotContain("servers/3/mail", cut.Markup);
        Assert.DoesNotContain("servers/3/docker", cut.Markup);
    }
}
