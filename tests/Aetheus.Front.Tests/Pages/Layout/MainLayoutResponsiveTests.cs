// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Tests.Pages;

public sealed class MainLayoutResponsiveTests : BunitContext
{
    public MainLayoutResponsiveTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void ColdRender_KeepsSidebarClosedAndViewportPending()
    {
        var cut = Render<MainLayout>();

        Assert.False(cut.Instance.IsSidebarExpanded);
        Assert.False(cut.Instance.IsViewportKnown);
        Assert.Contains("viewport-pending", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("sidebar-backdrop", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "Aetheus.hideSplash");
    }

    [Fact]
    public async Task InitialCompactViewport_ClosesDrawerBeforeSplashHandoff()
    {
        var cut = Render<MainLayout>();

        await cut.InvokeAsync(() => cut.Instance.OnViewportChanged(true));

        Assert.True(cut.Instance.IsMobileViewport);
        Assert.True(cut.Instance.IsViewportKnown);
        Assert.False(cut.Instance.IsSidebarExpanded);
        Assert.Contains("viewport-ready", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WiderViewport_RestoresDesktopRail()
    {
        var cut = Render<MainLayout>();

        await cut.InvokeAsync(() => cut.Instance.OnViewportChanged(false));

        Assert.False(cut.Instance.IsMobileViewport);
        Assert.True(cut.Instance.IsViewportKnown);
        Assert.True(cut.Instance.IsSidebarExpanded);
    }

    [Fact]
    public async Task CompactRoundTrip_PreservesManualDesktopRailState()
    {
        var cut = Render<MainLayout>();
        await cut.InvokeAsync(() => cut.Instance.OnViewportChanged(false));

        // ClickAsync, not Click: the synchronous overload does not hand back the dispatch task, so on a
        // busy renderer the assertion can run before the handler has toggled anything. Green here, red
        // on a loaded build agent - the same race that failed the dark-mode test on CI run 1166.
        await cut.Find("[aria-label='ToggleSidebar']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        Assert.False(cut.Instance.IsSidebarExpanded);

        await cut.InvokeAsync(() => cut.Instance.OnViewportChanged(true));
        await cut.InvokeAsync(() => cut.Instance.OnViewportChanged(false));

        Assert.False(cut.Instance.IsSidebarExpanded);
    }

    [Fact]
    public async Task CompactDrawer_FloatsWithItsVeil_AndNavigationToTheCurrentRouteClosesIt()
    {
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        var cut = Render<MainLayout>();
        await cut.InvokeAsync(() => cut.Instance.OnViewportChanged(true));
        // OnViewportChanged re-renders asynchronously. Clicking before that render settles raced
        // the toggle against it and made this test fail intermittently on CI, so wait for the
        // compact layout to be on screen before driving it.
        cut.WaitForState(() => cut.Instance.IsViewportKnown && cut.Instance.IsMobileViewport);
        await cut.Find("[aria-label='ToggleSidebar']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        cut.WaitForAssertion(() => Assert.True(cut.Instance.IsSidebarExpanded));

        // OE floats the drawer over the page with its veil, and both toggles say how to dismiss it.
        var sidebar = cut.FindComponent<OmniSidebar>();
        Assert.Equal(OmniSidebarReveal.Overlay, sidebar.Instance.Reveal);
        Assert.Single(cut.FindAll("#app-sidebar > .omni-sidebar__backdrop"));
        Assert.All(cut.FindComponents<OmniSidebarToggle>(), toggle =>
        {
            Assert.Equal(OmniSidebarReveal.Overlay, toggle.Instance.Reveal);
            Assert.Null(toggle.Instance.ChildContent);
            Assert.Contains(toggle.FindComponents<OmniIcon>(), icon => icon.Instance.Name == OmniIconName.Close);
        });

        // Choosing an entry navigates, even to the page already shown; OE closes the floating drawer
        // through OpenChanged, so nothing in Aetheus closes it by hand any more.
        await cut.InvokeAsync(() => nav.NavigateTo(nav.Uri));

        cut.WaitForAssertion(() => Assert.False(cut.Instance.IsSidebarExpanded));
        Assert.Empty(cut.FindAll(".omni-sidebar__backdrop"));
    }

    [Fact]
    public async Task DesktopSidebar_IsPushed_AndNavigationKeepsItOpen()
    {
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        var cut = Render<MainLayout>();
        await cut.InvokeAsync(() => cut.Instance.OnViewportChanged(false));
        cut.WaitForState(() => cut.Instance.IsViewportKnown && cut.Instance.IsSidebarExpanded);

        Assert.Equal(OmniSidebarReveal.Push, cut.FindComponent<OmniSidebar>().Instance.Reveal);
        var toggle = cut.FindComponents<OmniSidebarToggle>()[0];
        Assert.Equal(OmniSidebarReveal.Push, toggle.Instance.Reveal);

        await cut.InvokeAsync(() => nav.NavigateTo("/servers"));

        Assert.True(cut.Instance.IsSidebarExpanded);
        Assert.Empty(cut.FindAll(".omni-sidebar__backdrop"));
    }

    [Fact]
    public async Task DisposeAsync_AwaitsViewportWatcherTeardown()
    {
        var cut = Render<MainLayout>();

        await cut.Instance.DisposeAsync();

        Assert.Contains(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "Aetheus.disposeViewportWatcher");
    }
}
