// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs.Organizations;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NSubstitute;

namespace Aetheus.Front.Tests.Pages;

/// <summary>
/// Template coverage for MainLayout.razor driven by real renders and user
/// interactions (button/menu-row clicks) with assertions on markup and
/// navigation - complements <see cref="MainLayoutTemplateBranchTests"/>
/// (which covers breadcrumb / role badge / dark-mode / version banner branches).
/// </summary>
public class MainLayoutTemplateCoverageTests : BunitContext
{
    private BunitTestHelper.TestHandler RegisterWithAuth(bool authenticated = true, bool isAdmin = false)
    {
        var handler = BunitTestHelper.RegisterServices(this, authenticated, isAdmin);
        Services.AddScoped(sp => new TaskTrackerService(
            sp.GetRequiredService<ApiClient>(),
            sp.GetRequiredService<AuthStateProvider>(),
            sp.GetRequiredService<HubConnectionFactory>(),
            NullLogger<TaskTrackerService>.Instance));

        // MainLayout.OnInitializedAsync re-reads the token via the AuthStateProvider's IJSRuntime,
        // which the helper stubs to return null (wiping the token). Re-register the provider with a
        // JS stub returning a valid JWT so the authenticated chrome (user button, org area, menu)
        // actually renders.
        if (authenticated)
        {
            var js = Substitute.For<IJSRuntime>();
            js.InvokeAsync<string?>("localStorage.getItem", Arg.Is<object?[]>(a => a.Length == 1 && (string?)a[0] == "aetheus_auth_token"))
                .Returns(ValueTask.FromResult<string?>(MakeJwt(isAdmin)));
            var auth = new AuthStateProvider(js, NullLogger<AuthStateProvider>.Instance);
            Services.AddSingleton(auth);

            // The authenticated branch renders <NavMenu>, which depends on AlertNotificationService.
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ApiBaseUrl"] = "http://test:5301" })
                .Build();
            var hubFactory = new HubConnectionFactory(
                config, auth, NullLogger<AuthDelegatingHandler>.Instance);
            Services.AddSingleton(new AlertNotificationService(auth, hubFactory));
        }

        return handler;
    }

    private static string MakeJwt(bool isAdmin)
    {
        var header = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"HS256\"}"));
        object claims = isAdmin
            ? new { sub = "admin", role = "Admin", unique_name = "admin", exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() }
            : new { sub = "user", unique_name = "user", exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() };
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(claims);
        var p64 = Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{header}.{p64}.sig";
    }

    // ── Help button rendered for an authenticated user (line 50-51) ───────────

    [Fact]
    public void AuthenticatedUser_RendersHelpAndUserButtons()
    {
        RegisterWithAuth();
        var cut = Render<MainLayout>();

        // The help action button and the user button live in the auth header.
        Assert.Contains("header-action-btn", cut.Markup);
        Assert.Contains("display-name", cut.Markup);
    }

    // ── Skip-to-content link is always present once the chrome renders (line 12)

    [Fact]
    public void Renders_SkipToContentLink()
    {
        RegisterWithAuth();
        var cut = Render<MainLayout>();

        Assert.Contains("skip-link", cut.Markup);
        Assert.Contains("#main-content", cut.Markup);
    }

    // ── Admin user, orgs loaded, no available orgs → "AllOrganizations" badge ──
    // The org selector now lives in the user menu (relocated from the top bar): the
    // (Auth.IsAdmin && Orgs.IsLoaded) else-if branch renders once the menu is opened.

    [Fact]
    public void AdminUser_OrgsLoadedEmpty_RendersAllOrganizationsBadge()
    {
        RegisterWithAuth(authenticated: true, isAdmin: true);
        var orgs = Services.GetRequiredService<ActiveOrganizationService>();

        var cut = Render<MainLayout>();

        // Force the "loaded but empty" state so the admin badge branch renders.
        // (IsLoaded has a private setter; clicking the user button re-renders the menu.)
        typeof(ActiveOrganizationService)
            .GetProperty("IsLoaded")!
            .SetValue(orgs, true);
        cut.Find(".header-user-btn").Click();
        cut.WaitForElement(".user-menu-card", TimeSpan.FromSeconds(10));

        Assert.Contains("AllOrganizations", cut.Markup);
    }

    // ── Orgs loaded with available orgs → dropdown in the user menu ────────────
    // Replaces the former top-bar loading-placeholder test: the placeholder was
    // dropped when the picker moved into the (open-on-demand) user menu.

    [Fact]
    public void OrgsLoaded_RendersOrgPickerInUserMenu()
    {
        RegisterWithAuth();
        var orgs = Services.GetRequiredService<ActiveOrganizationService>();

        var cut = Render<MainLayout>();

        // Seed the "loaded with orgs" state AFTER the initial render (mirrors
        // AdminUser_OrgsLoadedEmpty_RendersAllOrganizationsBadge): seeding before the
        // render races with MainLayout.OnInitializedAsync's async org bootstrap, which
        // made this test flaky under coverage instrumentation on CI. Clicking the user
        // button re-renders the menu against the now-set state.
        typeof(ActiveOrganizationService).GetProperty("Available")!
            .SetValue(orgs, new List<MyOrganizationDto>
            {
                new(42, "Acme", "acme", OrganizationRole.Owner)
            });
        typeof(ActiveOrganizationService).GetProperty("IsLoaded")!.SetValue(orgs, true);
        cut.Find(".header-user-btn").Click();
        cut.WaitForElement(".user-menu-card", TimeSpan.FromSeconds(10));

        // The active-organization dropdown now renders inside the user menu.
        Assert.Contains("user-menu-org-picker", cut.Markup);
    }

    // ── Language label badge shows the current language (line 88) ─────────────

    [Fact]
    public void UserMenu_RendersCurrentLanguageBadge()
    {
        RegisterWithAuth();
        var cut = Render<MainLayout>();

        cut.Find(".header-user-btn").Click();
        cut.WaitForElement(".user-menu-card", TimeSpan.FromSeconds(10));

        // _currentLangLabel resolves to EN/FR depending on culture - assert one is shown.
        Assert.True(
            cut.Markup.Contains(">EN<") || cut.Markup.Contains(">FR<") ||
            cut.Markup.Contains("EN") || cut.Markup.Contains("FR"),
            "Expected a language badge (EN/FR) in the open user menu.");
        Assert.Contains("Language", cut.Markup);
    }

    // ── Settings menu row click → navigates to /settings (line 100) ───────────

    [Fact]
    public void UserMenu_SettingsRowClick_NavigatesToSettings()
    {
        RegisterWithAuth();
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        var cut = Render<MainLayout>();

        cut.Find(".header-user-btn").Click();
        cut.WaitForElement(".user-menu-card", TimeSpan.FromSeconds(10));

        // The settings row is the menu section whose click handler navigates to /settings.
        var settingsRow = cut.FindAll(".user-menu-section-clickable")
            .First(el => el.TextContent.Contains("Settings"));
        settingsRow.Click();

        // Observe the navigation produced by the rendered event callback. Under the full parallel
        // suite, bUnit can complete the click dispatch just after Click() returns even though the
        // handler itself is synchronous (the same interaction boundary as the async logout below).
        cut.WaitForAssertion(
            () => Assert.Contains("settings", nav.Uri),
            TimeSpan.FromSeconds(10));
    }

    // ── Logout menu row click → navigates to /login (line 106) ────────────────

    [Fact]
    public void UserMenu_LogoutRowClick_NavigatesToLogin()
    {
        RegisterWithAuth();
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        var cut = Render<MainLayout>();

        cut.Find(".header-user-btn").Click();
        cut.WaitForElement(".user-menu-card", TimeSpan.FromSeconds(10));

        var logoutRow = cut.FindAll(".user-menu-section-clickable")
            .First(el => el.TextContent.Contains("Logout"));
        logoutRow.Click();

        // The click handler awaits two localStorage removals before navigating. Under the full
        // parallel CI suite that continuation can complete just after Click() returns, so observe
        // the user-visible navigation instead of racing the async logout continuation.
        cut.WaitForAssertion(
            () => Assert.Contains("login", nav.Uri),
            TimeSpan.FromSeconds(10));
    }

    // ── User-menu backdrop click closes the menu (line 77) ────────────────────

    [Fact]
    public void UserMenu_BackdropClick_ClosesMenu()
    {
        RegisterWithAuth();
        var cut = Render<MainLayout>();

        cut.Find(".header-user-btn").Click();
        cut.WaitForElement(".user-menu-card", TimeSpan.FromSeconds(10));
        Assert.Contains("user-menu-card", cut.Markup);

        cut.Find(".user-menu-backdrop").Click();
        Assert.DoesNotContain("user-menu-card", cut.Markup);
    }
}
