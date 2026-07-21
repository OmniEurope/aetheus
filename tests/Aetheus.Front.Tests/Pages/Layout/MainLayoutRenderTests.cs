// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Layout;
using Aetheus.Front.Services;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NSubstitute;

namespace Aetheus.Front.Tests.Pages;

/// <summary>
/// Tests MainLayout logic methods via reflection. Uses BunitContext so that bUnit's
/// NavigationManager (which is properly initialized) is available for injection.
/// </summary>
public class MainLayoutRenderTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly BindingFlags PrivPub = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;

    public MainLayoutRenderTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    private MainLayout CreateInstance(string path = "servers")
    {
        var instance = new MainLayout();

        // Use bUnit's registered NavigationManager (properly initialized)
        var nav = Services.GetRequiredService<NavigationManager>();
        typeof(MainLayout).GetProperty("Nav", Priv)!.SetValue(instance, nav);

        // Wire JS
        var js = Substitute.For<IJSRuntime>();
        typeof(MainLayout).GetProperty("JS", Priv)!.SetValue(instance, js);

        // Wire L (localizer)
        typeof(MainLayout).GetProperty("L", Priv)!.SetValue(instance, new BunitTestHelper.StubLocalizer());

        // Wire Logger
        typeof(MainLayout).GetProperty("Logger", Priv)!
            .SetValue(instance, NullLogger<MainLayout>.Instance);

        // Wire AuthStateProvider (unauthenticated stub)
        var authJs = Substitute.For<IJSRuntime>();
        var auth = new AuthStateProvider(authJs, NullLogger<AuthStateProvider>.Instance);
        typeof(MainLayout).GetProperty("Auth", Priv)!.SetValue(instance, auth);

        // Wire PermissionService
        var perms = new PermissionService();
        typeof(MainLayout).GetProperty("Permissions", Priv)!.SetValue(instance, perms);

        // Wire BreadcrumbService
        var breadcrumb = new BreadcrumbService(nav);
        typeof(MainLayout).GetProperty("Breadcrumb", Priv)!.SetValue(instance, breadcrumb);

        // Wire HelpService
        var http = new HttpClient { BaseAddress = new Uri("http://localhost/") };
        var help = new HelpService(http);
        typeof(MainLayout).GetProperty("Help", Priv)!.SetValue(instance, help);

        // Wire ApiClient
        var testHandler = new BunitTestHelper.TestHandler();
        var apiHttp = new HttpClient(testHandler) { BaseAddress = new Uri("http://localhost/") };
        var api = new ApiClient(apiHttp);
        typeof(MainLayout).GetProperty("Api", Priv)!.SetValue(instance, api);

        // Wire Configuration
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:Version"] = "1.0.0" })
            .Build();
        typeof(MainLayout).GetProperty("Configuration", Priv)!.SetValue(instance, config);

        // Wire ActiveOrganizationService
        var orgJs = Substitute.For<IJSRuntime>();
        var orgs = new ActiveOrganizationService(api, orgJs);
        typeof(MainLayout).GetProperty("Orgs", Priv)!.SetValue(instance, orgs);

        // Wire ListCacheService (cleared on sign-out / org switch)
        typeof(MainLayout).GetProperty("Cache", Priv)!.SetValue(instance, new ListCacheService(TimeProvider.System));

        // Wire IHttpClientFactory
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient { BaseAddress = new Uri("http://localhost/") });
        typeof(MainLayout).GetProperty("HttpFactory", Priv)!.SetValue(instance, httpFactory);

        return instance;
    }

    [Fact]
    public async Task ToggleDarkMode_FlipsDarkModeValue()
    {
        var instance = CreateInstance();
        typeof(MainLayout).GetField("_darkMode", Priv)!.SetValue(instance, true);

        var method = typeof(MainLayout).GetMethod("ToggleDarkMode", PrivPub)!;
        await (Task)method.Invoke(instance, [])!;

        var darkMode = (bool)typeof(MainLayout).GetField("_darkMode", Priv)!.GetValue(instance)!;
        Assert.False(darkMode);
    }

    [Fact]
    public async Task ToggleDarkMode_WhenFalse_SetsTrue()
    {
        var instance = CreateInstance();
        typeof(MainLayout).GetField("_darkMode", Priv)!.SetValue(instance, false);

        var method = typeof(MainLayout).GetMethod("ToggleDarkMode", PrivPub)!;
        await (Task)method.Invoke(instance, [])!;

        var darkMode = (bool)typeof(MainLayout).GetField("_darkMode", Priv)!.GetValue(instance)!;
        Assert.True(darkMode);
    }

    [Fact]
    public async Task OnLogout_ClearsPermissions()
    {
        var instance = CreateInstance();

        var perms = (PermissionService)typeof(MainLayout).GetProperty("Permissions", Priv)!.GetValue(instance)!;

        var method = typeof(MainLayout).GetMethod("OnLogout", PrivPub)!;
        await (Task)method.Invoke(instance, [])!;

        Assert.False(perms.IsLoaded);
    }

    [Fact]
    public void NavigateToHelp_WithKnownPage_NavigatesToHelpRoute()
    {
        var instance = CreateInstance();
        var nav = (NavigationManager)typeof(MainLayout).GetProperty("Nav", Priv)!.GetValue(instance)!;

        // bUnit default URI is http://localhost/ (empty path) → resolves to the "dashboard" help key,
        // so NavigateToHelp lands on a /help route rather than merely not throwing.
        var method = typeof(MainLayout).GetMethod("NavigateToHelp", PrivPub)!;
        method.Invoke(instance, []);

        Assert.Contains("help", nav.Uri);
    }

    [Fact]
    public void NavigateToHelp_WithUnknownPage_FallsBackToHelpIndex()
    {
        var instance = CreateInstance();

        // Navigate to something not in the help map
        var nav = (NavigationManager)typeof(MainLayout).GetProperty("Nav", Priv)!.GetValue(instance)!;
        nav.NavigateTo("/some-unknown-page");

        var method = typeof(MainLayout).GetMethod("NavigateToHelp", PrivPub)!;
        method.Invoke(instance, []);

        // An unresolved page key falls back to the /help index (not a /help/{key} article).
        Assert.EndsWith("/help", nav.Uri);
    }

    [Fact]
    public void OnReloadClick_ForceLoadsCurrentUri()
    {
        var instance = CreateInstance();
        var nav = (Bunit.TestDoubles.BunitNavigationManager)typeof(MainLayout).GetProperty("Nav", Priv)!.GetValue(instance)!;
        var uri = nav.Uri;

        var method = typeof(MainLayout).GetMethod("OnReloadClick", PrivPub)!;
        method.Invoke(instance, []);

        // Reload re-navigates to the current URI with forceLoad → a full-page reload of the same route.
        var last = nav.History.Last();
        Assert.Equal(uri, last.Uri);
        Assert.True(last.Options.ForceLoad);
    }

    [Fact]
    public void RedirectIfUnauthenticated_WhenOnLogin_DoesNotNavigateAgain()
    {
        var instance = CreateInstance();
        var nav = (Bunit.TestDoubles.BunitNavigationManager)typeof(MainLayout).GetProperty("Nav", Priv)!.GetValue(instance)!;
        nav.NavigateTo("/login");
        var historyCount = nav.History.Count;

        // Auth is unauthenticated but already on /login - the guard must NOT re-navigate (no loop):
        // the URI stays on login and no extra history entry is pushed.
        var method = typeof(MainLayout).GetMethod("RedirectIfUnauthenticated", Priv)!;
        method.Invoke(instance, []);

        Assert.Contains("login", nav.Uri);
        Assert.Equal(historyCount, nav.History.Count);
    }

    [Fact]
    public void RedirectIfUnauthenticated_WhenNotOnLogin_NavigatesToLogin()
    {
        var instance = CreateInstance();
        var nav = (NavigationManager)typeof(MainLayout).GetProperty("Nav", Priv)!.GetValue(instance)!;

        // bUnit navigation starts at http://localhost/ (empty path) → not "login". An unauthenticated
        // user on a non-login route must be bounced to /login.
        var method = typeof(MainLayout).GetMethod("RedirectIfUnauthenticated", Priv)!;
        method.Invoke(instance, []);

        Assert.Contains("login", nav.Uri);
    }

    [Fact]
    public async Task OnActiveOrgChanged_WithIntValue_SelectsActiveOrg()
    {
        var instance = CreateInstance();
        var orgs = (ActiveOrganizationService)typeof(MainLayout).GetProperty("Orgs", Priv)!.GetValue(instance)!;
        // Seed the available orgs so the int branch can resolve id 1 and switch the active org.
        typeof(ActiveOrganizationService).GetProperty("Available")!
            .SetValue(orgs, new List<Aetheus.Shared.DTOs.Organizations.MyOrganizationDto>
            {
                new(1, "Acme", "acme", Aetheus.Shared.Enums.OrganizationRole.Owner)
            });

        var method = typeof(MainLayout).GetMethod("OnActiveOrgChanged", PrivPub)!;
        await (Task)method.Invoke(instance, [(object)1])!;

        // The int branch dispatches to SetActiveAsync → the active org becomes the matched id.
        Assert.Equal(1, orgs.Active?.Id);
    }

    [Fact]
    public async Task OnActiveOrgChanged_WithNonIntValue_SelectsNoOrg()
    {
        var instance = CreateInstance();
        var orgs = (ActiveOrganizationService)typeof(MainLayout).GetProperty("Orgs", Priv)!.GetValue(instance)!;
        typeof(ActiveOrganizationService).GetProperty("Available")!
            .SetValue(orgs, new List<Aetheus.Shared.DTOs.Organizations.MyOrganizationDto>
            {
                new(1, "Acme", "acme", Aetheus.Shared.Enums.OrganizationRole.Owner)
            });

        var method = typeof(MainLayout).GetMethod("OnActiveOrgChanged", PrivPub)!;
        await (Task)method.Invoke(instance, [(object)"not-an-int"])!;

        // A non-int value short-circuits to Task.CompletedTask: no active org is selected.
        Assert.Null(orgs.Active);
    }

    [Fact]
    public void RecoverError_WithNullBoundary_NoOps()
    {
        var instance = CreateInstance();
        // No ErrorBoundary has been captured yet - the `_errorBoundary?.Recover()`
        // null-conditional must short-circuit and leave the field untouched rather than NRE.
        Assert.Null(typeof(MainLayout).GetField("_errorBoundary", Priv)!.GetValue(instance));

        var method = typeof(MainLayout).GetMethod("RecoverError", PrivPub)!;
        var ex = Record.Exception(() => method.Invoke(instance, []));

        Assert.Null(ex);
        Assert.Null(typeof(MainLayout).GetField("_errorBoundary", Priv)!.GetValue(instance));
    }

    [Fact]
    public void OnBreadcrumbChanged_OnBreadcrumbSet_ReRendersLayout()
    {
        // Render a real MainLayout so its OnInitializedAsync subscribes OnBreadcrumbChanged to
        // Breadcrumb.OnChanged. Firing a breadcrumb change must drive a re-render (StateHasChanged).
        var cut = Render<MainLayout>();
        var before = cut.RenderCount;
        var breadcrumb = Services.GetRequiredService<BreadcrumbService>();

        cut.InvokeAsync(() => breadcrumb.Set(new BreadcrumbItem("Home", "/")));

        Assert.True(cut.RenderCount > before);
    }

    [Fact]
    public void CloseUserMenu_SetsMenuClosed()
    {
        var instance = CreateInstance();
        typeof(MainLayout).GetField("_userMenuOpen", Priv)!.SetValue(instance, true);

        var method = typeof(MainLayout).GetMethod("CloseUserMenu", Priv)!;
        method.Invoke(instance, []);

        var menuOpen = (bool)typeof(MainLayout).GetField("_userMenuOpen", Priv)!.GetValue(instance)!;
        Assert.False(menuOpen);
    }

    [Fact]
    public void HandleUserMenuKeyDown_Escape_ClosesMenu()
    {
        var instance = CreateInstance();
        typeof(MainLayout).GetField("_userMenuOpen", Priv)!.SetValue(instance, true);

        var method = typeof(MainLayout).GetMethod("HandleUserMenuKeyDown", Priv)!;
        var args = new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" };
        method.Invoke(instance, [args]);

        var menuOpen = (bool)typeof(MainLayout).GetField("_userMenuOpen", Priv)!.GetValue(instance)!;
        Assert.False(menuOpen);
    }

    [Fact]
    public void HandleUserMenuKeyDown_OtherKey_DoesNotCloseMenu()
    {
        var instance = CreateInstance();
        typeof(MainLayout).GetField("_userMenuOpen", Priv)!.SetValue(instance, true);

        var method = typeof(MainLayout).GetMethod("HandleUserMenuKeyDown", Priv)!;
        var args = new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" };
        method.Invoke(instance, [args]);

        var menuOpen = (bool)typeof(MainLayout).GetField("_userMenuOpen", Priv)!.GetValue(instance)!;
        Assert.True(menuOpen);
    }

    [Fact]
    public void ToggleUserMenu_OpensMenu()
    {
        var instance = CreateInstance();
        typeof(MainLayout).GetField("_userMenuOpen", Priv)!.SetValue(instance, false);

        var method = typeof(MainLayout).GetMethod("ToggleUserMenu", Priv)!;
        method.Invoke(instance, []);

        var menuOpen = (bool)typeof(MainLayout).GetField("_userMenuOpen", Priv)!.GetValue(instance)!;
        Assert.True(menuOpen);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var instance = CreateInstance();

        var ex = Record.Exception(() => instance.Dispose());
        Assert.Null(ex);
    }
}
