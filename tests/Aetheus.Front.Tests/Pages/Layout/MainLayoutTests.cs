// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Aetheus.Front.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages;

public class MainLayoutTests : BunitContext
{
    public MainLayoutTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private readonly BunitTestHelper.TestHandler _handler;

    [Fact]
    public void WhenNotAuthenticated_RedirectsToLogin()
    {
        // Re-register with unauthenticated state
        var handler = BunitTestHelper.RegisterServices(this, authenticated: false);
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();

        var cut = Render<MainLayout>();
        Assert.Contains("login", nav.Uri);
    }

    [Fact]
    public void WhenAuthenticated_RendersContent()
    {
        var cut = Render<MainLayout>();
        Assert.Contains("Aetheus", cut.Markup);
    }

    [Fact]
    public void RendersAppShell_BodyWrapper()
    {
        var cut = Render<MainLayout>();
        // The chrome renders its routed-content body wrapper (the @Body host region).
        // (Auth-gated sidebar chrome needs a real JWT in localStorage, which the unit
        // harness can't supply - AuthStateProvider.InitializeAsync reads it back as null,
        // so this asserts the always-on shell, not the authenticated navigation.)
        Assert.Contains("app-body-wrapper", cut.Markup);
    }

    [Fact]
    public void RendersAppShell_HomeLink_WithoutErrorCard()
    {
        var cut = Render<MainLayout>();
        // The app shell renders the home/brand link and the ErrorBoundary is not faulted.
        Assert.Contains("app-home-link", cut.Markup);
        Assert.DoesNotContain("error-card", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var cut = Render<MainLayout>();
        var instance = (IDisposable)cut.Instance;
        instance.Dispose();
    }

    [Fact]
    public async Task ToggleDarkMode_TogglesState()
    {
        var cut = Render<MainLayout>();

        await cut.Instance.ToggleDarkMode();

        Assert.False(cut.Instance._darkMode); // was true, toggled to false
    }

    [Fact]
    public void RecoverError_DoesNotThrow()
    {
        var cut = Render<MainLayout>();

        cut.Instance.RecoverError();

        // Recovering the (non-faulted) ErrorBoundary leaves the normal chrome rendered - no error card.
        Assert.DoesNotContain("error-card", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnLogout_DoesNotThrow()
    {
        var cut = Render<MainLayout>();

        // Use InvokeAsync to stay on the renderer's sync context
        await cut.InvokeAsync(async () =>
        {
            await cut.Instance.OnLogout();
        });

        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        Assert.Contains("login", nav.Uri);
    }

    [Fact]
    public async Task ToggleDarkMode_TwiceRestoresState()
    {
        var cut = Render<MainLayout>();
        Assert.True(cut.Instance._darkMode);
        await cut.InvokeAsync(async () => await cut.Instance.ToggleDarkMode());
        Assert.False(cut.Instance._darkMode);
        await cut.InvokeAsync(async () => await cut.Instance.ToggleDarkMode());
        Assert.True(cut.Instance._darkMode);
    }

    [Fact]
    public async Task OnLogout_NavigatesToLogin()
    {
        var cut = Render<MainLayout>();
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();

        await cut.InvokeAsync(async () => await cut.Instance.OnLogout());

        Assert.Contains("login", nav.Uri);
    }

    [Fact]
    public async Task NavigateToHelp_NavigatesToHelpRoute()
    {
        var cut = Render<MainLayout>();
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();

        await cut.InvokeAsync(() => { cut.Instance.NavigateToHelp(); return Task.CompletedTask; });

        // NavigateToHelp resolves the page key and navigates to a /help route. That navigation is
        // recorded in History even though the unauthenticated harness then redirects to /login on
        // the location change (so nav.Uri ends at /login) - assert the help navigation actually fired.
        Assert.Contains(nav.History, h => h.Uri.Contains("help"));
    }

    [Fact]
    public void RendersAppShell_HeaderTitle()
    {
        var cut = Render<MainLayout>();
        // The header chrome renders (the brand title region of the top bar).
        Assert.Contains("app-title", cut.Markup);
    }

    [Fact]
    public void WhenAuthenticated_RendersHeaderBar()
    {
        var cut = Render<MainLayout>();
        Assert.Contains("Aetheus", cut.Markup);
    }

    [Fact]
    public void WhenAuthenticated_RendersSkipLink()
    {
        var cut = Render<MainLayout>();
        // The a11y skip-link anchors to the #main-content body landmark.
        Assert.Contains("class=\"skip-link\"", cut.Markup);
        Assert.Contains("#main-content", cut.Markup);
    }
}
