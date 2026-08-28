// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NSubstitute;

namespace Aetheus.Front.Tests.Pages;

/// <summary>
/// Template-branch coverage for MainLayout.razor - exercised by RENDERING the
/// component and asserting on the produced markup / navigation, not by reading
/// fields via reflection. Each test drives a real @if branch of the template.
/// </summary>
public class MainLayoutTemplateBranchTests : BunitContext
{
    private BunitTestHelper.TestHandler RegisterWithTaskTracker(bool authenticated = true, bool isAdmin = false)
    {
        var handler = BunitTestHelper.RegisterServices(this, authenticated, isAdmin);
        handler.SetResponse(HttpMethod.Get, "health/live", System.Net.HttpStatusCode.ServiceUnavailable);
        Services.AddScoped(sp => new TaskTrackerService(
            sp.GetRequiredService<ApiClient>(),
            sp.GetRequiredService<AuthStateProvider>(),
            sp.GetRequiredService<HubConnectionFactory>(),
            NullLogger<TaskTrackerService>.Instance));

        // MainLayout.OnInitializedAsync calls Auth.InitializeAsync(), which RE-READS the token
        // from localStorage via the AuthStateProvider's own IJSRuntime. The helper wires that
        // provider with an NSubstitute IJSRuntime returning null, so the token is wiped and the
        // authenticated chrome never renders. Re-register the provider with a JS stub that
        // returns a valid JWT, so the real init path keeps the user authenticated.
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

    // ── Authenticated user → header chrome + user button rendered ─────────────
    // Covers: Auth.IsAuthenticated branch (header actions block, lines 17-116).

    [Fact]
    public void AuthenticatedUser_RendersHeaderChromeAndUserButton()
    {
        RegisterWithTaskTracker(authenticated: true);

        var cut = Render<MainLayout>();

        // Title and the username (auth header) appear only in the authenticated branch.
        Assert.Contains("Aetheus", cut.Markup);
        Assert.Contains("header-user-btn", cut.Markup);
        // Sidebar toggle is gated on Auth.IsAuthenticated.
        Assert.Contains("header-bar", cut.Markup);
    }

    // ── Unauthenticated user → redirect to /login, no header actions ──────────
    // Covers: Auth.IsAuthenticated = false (header actions / sidebar skipped).

    [Fact]
    public void UnauthenticatedUser_RedirectsToLogin_NoUserMenu()
    {
        RegisterWithTaskTracker(authenticated: false);
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();

        var cut = Render<MainLayout>();

        Assert.Contains("login", nav.Uri);
        // The authenticated user button must NOT be rendered for an anonymous user.
        Assert.DoesNotContain("header-user-btn", cut.Markup);
    }

    [Fact]
    public void BreadcrumbItems_RenderInFixedGlobalSlot()
    {
        RegisterWithTaskTracker();
        var breadcrumb = Services.GetRequiredService<BreadcrumbService>();
        var cut = Render<MainLayout>();

        cut.InvokeAsync(() => breadcrumb.Set([
            new BreadcrumbItem("Servers", "/servers"),
            new BreadcrumbItem("web-01", null)
        ]));
        cut.Render();

        Assert.Contains("app-breadcrumb", cut.Markup);
        Assert.Contains("Servers", cut.Markup);
        Assert.Contains("web-01", cut.Markup);
        Assert.DoesNotContain("breadcrumb-slot", cut.Markup);
    }

    // ── User menu open → role badge + language/dark-mode/settings/logout rows ─
    // Covers lines 75-113 (the @if (_userMenuOpen) dropdown card).

    [Fact]
    public void UserMenu_WhenOpened_RendersRoleAndMenuSections()
    {
        RegisterWithTaskTracker(authenticated: true, isAdmin: false);
        var cut = Render<MainLayout>();

        // Closed by default - the dropdown card is absent.
        Assert.DoesNotContain("user-menu-card", cut.Markup);

        // Click the user button - the real path that opens the menu.
        cut.Find(".header-user-btn").Click();
        cut.WaitForElement(".user-menu-card");

        // Open → the card with the localized menu rows is rendered.
        Assert.Contains("user-menu-card", cut.Markup);
        Assert.Contains("Logout", cut.Markup);
        Assert.Contains("Settings", cut.Markup);
        // Non-admin user → "User" role label (not "Admin").
        Assert.Contains("User", cut.Markup);
    }

    // ── Admin user, menu open → "Admin" role badge (line 81 true branch) ──────

    [Fact]
    public void UserMenu_AdminUser_RendersAdminRoleBadge()
    {
        RegisterWithTaskTracker(authenticated: true, isAdmin: true);
        var cut = Render<MainLayout>();

        cut.Find(".header-user-btn").Click();
        cut.WaitForElement(".user-menu-card");

        Assert.Contains("Admin", cut.Markup);
    }

    // ── Dark mode default → "dark_mode" icon; toggle flips to "light_mode" ────
    // Covers the @(_darkMode ? "dark_mode" : "light_mode") expression (line 94).

    [Fact]
    public async Task DarkMode_DefaultIcon_AndRowTogglesState()
    {
        RegisterWithTaskTracker();
        var cut = Render<MainLayout>();

        cut.Find(".header-user-btn").Click();
        cut.WaitForElement(".user-menu-card");
        // Default dark mode true → dark_mode icon + DarkMode label rendered.
        Assert.Contains("dark_mode", cut.Markup);
        Assert.Contains("DarkMode", cut.Markup);
        Assert.True(cut.Instance._darkMode);

        // Click the dark-mode menu row - the real UI path that flips the theme.
        // ClickAsync, not Click: the synchronous overload does not return the dispatch task, so with
        // an `async Task` handler like ToggleDarkMode it is fire-and-forget. When the renderer's
        // dispatcher is already busy (the version monitor re-rendering, or plain CPU contention on a
        // loaded build agent) the handler had not even reached its first line when the assertion ran,
        // and the test failed with Expected: False / Actual: True - exactly the CI flake on run 1166.
        var darkRow = cut.FindAll(".user-menu-section-clickable")
            .First(el => el.TextContent.Contains("DarkMode"));
        await darkRow.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Observable effect: the theme flag flipped to light mode.
        Assert.False(cut.Instance._darkMode);
    }

    // ── _newVersionAvailable = true → version alert banner (lines 133-140) ────

    [Fact]
    public void NewVersionAvailable_RendersReloadAlertBanner()
    {
        RegisterWithTaskTracker();
        var cut = Render<MainLayout>();

        // Default: no banner.
        Assert.DoesNotContain("version-alert-bar", cut.Markup);

        // Simulate the version-check loop flagging an update.
        cut.InvokeAsync(() => { cut.Instance._newVersionAvailable = true; });
        cut.Render();

        Assert.Contains("version-alert-bar", cut.Markup);
        Assert.Contains("NewVersionAvailable", cut.Markup);
        Assert.Contains("ReloadNow", cut.Markup);
    }
}
