// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Reflection;
using Aetheus.Front.Layout;
using Aetheus.Front.Services;
using Aetheus.Front.Tests.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.DTOs.Organizations;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages;

/// <summary>
/// Method-level coverage for MainLayout.razor.cs - targets the methods that currently have
/// zero coverage: LoadPermissionsAsync, LoadOrganizationsAsync, CheckForNewVersionAsync,
/// ToggleLanguage, OnNeedsLogin, OnSettingsMenuClick, OnLogoutMenuClick.
///
/// Uses rendering (the layout is renderable via BunitContext) plus reflection-based invocation
/// for methods that are private or hard to trigger through UI events alone.
/// </summary>
public class MainLayoutMethodCoverageTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type LayoutType = typeof(MainLayout);
    private readonly BunitTestHelper.TestHandler _handler;

    public MainLayoutMethodCoverageTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    // ── LoadPermissionsAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task LoadPermissionsAsync_SuccessfulResponse_SetsPermissions()
    {
        var summary = new UserPermissionSummaryDto
        {
            UserId = 1,
            Username = "admin",
            Roles = ["Admin"],
            EffectivePermissions = Enum.GetValues<ResourceType>()
                .SelectMany(rt => new[]
                {
                    new EffectivePermissionDto { ResourceType = rt, ResourceId = null, Permission = Permission.Read },
                    new EffectivePermissionDto { ResourceType = rt, ResourceId = null, Permission = Permission.Write }
                })
                .ToList()
        };
        _handler.SetJsonResponse("api/users/me/permissions", summary);

        var cut = Render<MainLayout>();
        _handler.Requests.Clear();
        var method = LayoutType.GetMethod("LoadPermissionsAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // LoadPermissionsAsync fetches the user's effective permissions and feeds them to PermissionService.
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/users/me/permissions"));
        Assert.True(Services.GetRequiredService<PermissionService>().IsLoaded);
    }

    [Fact]
    public async Task LoadPermissionsAsync_HttpError_StillCallsTheApi()
    {
        _handler.SetResponse("api/users/me/permissions", HttpStatusCode.InternalServerError);

        var cut = Render<MainLayout>();
        _handler.Requests.Clear();
        var method = LayoutType.GetMethod("LoadPermissionsAsync", Priv)!;

        // Should swallow the exception (logs a warning and returns) after attempting the fetch.
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/users/me/permissions"));
    }

    [Fact]
    public async Task LoadPermissionsAsync_NullResponse_DoesNotSetPermissions()
    {
        // Return a JSON `null` body so GetMyPermissionsAsync genuinely returns null (a 204/{} body
        // would deserialize to a non-null DTO and wrongly trip SetPermissions).
        _handler.SetJsonResponse<UserPermissionSummaryDto?>("api/users/me/permissions", null);

        var cut = Render<MainLayout>();
        _handler.Requests.Clear();
        // A null permissions payload must NOT re-trigger SetPermissions (which raises OnPermissionsChanged).
        var permissions = Services.GetRequiredService<PermissionService>();
        var changedFired = false;
        permissions.OnPermissionsChanged += () => changedFired = true;

        var method = LayoutType.GetMethod("LoadPermissionsAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/users/me/permissions"));
        Assert.False(changedFired);
    }

    // ── LoadOrganizationsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task LoadOrganizationsAsync_SuccessfulResponse_CompletesWithoutThrow()
    {
        // GetMyOrganizationsAsync endpoint
        _handler.SetJsonResponse("api/organizations/me", new List<object>());

        var cut = Render<MainLayout>();
        _handler.Requests.Clear();
        var method = LayoutType.GetMethod("LoadOrganizationsAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // LoadOrganizationsAsync delegates to ActiveOrganizationService.RefreshAsync → GET api/organizations/me.
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/organizations/me"));
    }

    [Fact]
    public async Task LoadOrganizationsAsync_HttpError_StillCallsTheApi()
    {
        _handler.SetResponse("api/organizations/me", HttpStatusCode.InternalServerError);

        var cut = Render<MainLayout>();
        _handler.Requests.Clear();
        var method = LayoutType.GetMethod("LoadOrganizationsAsync", Priv)!;
        // Swallows the 500 after attempting the fetch.
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/organizations/me"));
    }

    // ── CheckForNewVersionAsync ────────────────────────────────────────────────

    [Fact]
    public async Task CheckForNewVersionAsync_SameVersion_ReturnsFalse()
    {
        // The method calls GetStringAsync on appsettings.json. We stub the HttpClient
        // the factory creates (BunitTestHelper already wires a mock factory returning a
        // client backed by handler). We stub "appsettings.json" in the handler so the
        // client returns matching version content.
        var cut = Render<MainLayout>();

        // Read the current _version field (defaults to "dev")
        var currentVersion = (string)LayoutType.GetField("_version", Priv)!.GetValue(cut.Instance)!;

        // Build an HttpClient that will return a JSON with the same version
        var appSettingsJson = $"{{\"App\":{{\"Version\":\"{currentVersion}\"}}}}";
        var fakeClient = new HttpClient(new StaticResponseHandler(appSettingsJson))
        {
            BaseAddress = new Uri("http://test/")
        };

        var method = LayoutType.GetMethod("CheckForNewVersionAsync", Priv)!;
        var result = await (Task<bool>)method.Invoke(cut.Instance, [fakeClient, CancellationToken.None])!;

        Assert.False(result);
        Assert.False(cut.Instance._newVersionAvailable);
    }

    [Fact]
    public async Task CheckForNewVersionAsync_DifferentVersion_SetsNewVersionAvailableTrue()
    {
        var cut = Render<MainLayout>();

        // Force a known _version value
        LayoutType.GetField("_version", Priv)!.SetValue(cut.Instance, "1.0.0");

        var appSettingsJson = "{\"App\":{\"Version\":\"2.0.0\"}}";
        var fakeClient = new HttpClient(new StaticResponseHandler(appSettingsJson))
        {
            BaseAddress = new Uri("http://test/")
        };

        var method = LayoutType.GetMethod("CheckForNewVersionAsync", Priv)!;
        // InvokeAsync so InvokeAsync(StateHasChanged) inside the method stays on the renderer thread
        var result = await cut.InvokeAsync(async () =>
            await (Task<bool>)method.Invoke(cut.Instance, [fakeClient, CancellationToken.None])!);

        Assert.True(result);
        Assert.True(cut.Instance._newVersionAvailable);
    }

    [Fact]
    public async Task CheckForNewVersionAsync_MissingAppSection_ReturnsFalse()
    {
        var cut = Render<MainLayout>();
        LayoutType.GetField("_version", Priv)!.SetValue(cut.Instance, "1.0.0");

        // JSON without "App" section
        var appSettingsJson = "{\"OtherSection\":{\"Key\":\"value\"}}";
        var fakeClient = new HttpClient(new StaticResponseHandler(appSettingsJson))
        {
            BaseAddress = new Uri("http://test/")
        };

        var method = LayoutType.GetMethod("CheckForNewVersionAsync", Priv)!;
        var result = await (Task<bool>)method.Invoke(cut.Instance, [fakeClient, CancellationToken.None])!;

        Assert.False(result);
    }

    [Fact]
    public async Task CheckForNewVersionAsync_EmptyRemoteVersion_ReturnsFalse()
    {
        var cut = Render<MainLayout>();
        LayoutType.GetField("_version", Priv)!.SetValue(cut.Instance, "1.0.0");

        // Version key exists but is empty
        var appSettingsJson = "{\"App\":{\"Version\":\"\"}}";
        var fakeClient = new HttpClient(new StaticResponseHandler(appSettingsJson))
        {
            BaseAddress = new Uri("http://test/")
        };

        var method = LayoutType.GetMethod("CheckForNewVersionAsync", Priv)!;
        var result = await (Task<bool>)method.Invoke(cut.Instance, [fakeClient, CancellationToken.None])!;

        Assert.False(result);
    }

    // ── ToggleLanguage ────────────────────────────────────────────────────────

    [Fact]
    public async Task ToggleLanguage_ClosesUserMenu()
    {
        var cut = Render<MainLayout>();

        // Open user menu first
        LayoutType.GetField("_userMenuOpen", Priv)!.SetValue(cut.Instance, true);

        var method = LayoutType.GetMethod("ToggleLanguage", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // _userMenuOpen should be false after ToggleLanguage runs
        var menuOpen = (bool)LayoutType.GetField("_userMenuOpen", Priv)!.GetValue(cut.Instance)!;
        Assert.False(menuOpen);
    }

    [Fact]
    public async Task ToggleLanguage_StillInvokesJs()
    {
        var cut = Render<MainLayout>();
        var method = LayoutType.GetMethod("ToggleLanguage", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // ToggleLanguage persists the new culture and applies it through the JS interop layer.
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "Aetheus.setLang");
    }

    // ── OnNeedsLogin ──────────────────────────────────────────────────────────

    [Fact]
    public async Task OnNeedsLogin_WhenNotOnLoginPage_Triggers()
    {
        UseCountingHubFactory();
        var cut = Render<MainLayout>();
        var factory = (CountingHubConnectionFactory)Services.GetRequiredService<HubConnectionFactory>();
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        var permissions = Services.GetRequiredService<PermissionService>();
        var cache = Services.GetRequiredService<ListCacheService>();
        permissions.SetPermissions(
            [new EffectivePermissionDto { ResourceType = ResourceType.Project, Permission = Permission.Read }],
            isAdmin: false);
        cache.Set("dashboard:overview", new DashboardOverviewDto { TotalServers = 1 });

        var method = LayoutType.GetMethod("OnNeedsLogin", Priv)!;
        // Await the dispatch so the assertion runs after OnNeedsLogin's internal InvokeAsync completes.
        await cut.InvokeAsync(() =>
        {
            method.Invoke(cut.Instance, []);
            return Task.CompletedTask;
        });

        // Not on the login page, so the 401 funnel redirects to /login.
        Assert.Contains("login", nav.Uri);
        cut.WaitForAssertion(() => Assert.Equal(1, factory.StopAllCount));
        Assert.False(permissions.IsLoaded);
        Assert.False(cache.TryGet<DashboardOverviewDto>("dashboard:overview", out _));
    }

    // ── OnSettingsMenuClick ───────────────────────────────────────────────────

    [Fact]
    public async Task OnSettingsMenuClick_ClosesMenuAndNavigatesToSettings()
    {
        var cut = Render<MainLayout>();
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        LayoutType.GetField("_userMenuOpen", Priv)!.SetValue(cut.Instance, true);

        var method = LayoutType.GetMethod("OnSettingsMenuClick", Priv)!;
        await cut.InvokeAsync(() =>
        {
            method.Invoke(cut.Instance, []);
            return Task.CompletedTask;
        });

        // The click closes the user menu AND navigates to the settings route. Assert on the navigation
        // history (not the final Uri): MainLayout's unauthenticated-redirect guard can bounce the final
        // location to /login after the settings navigation, but the /settings navigation still happened.
        var menuOpen = (bool)LayoutType.GetField("_userMenuOpen", Priv)!.GetValue(cut.Instance)!;
        Assert.False(menuOpen);
        Assert.Contains(nav.History, h => h.Uri.EndsWith("/settings"));
    }

    // ── OnLogoutMenuClick ─────────────────────────────────────────────────────

    [Fact]
    public async Task OnLogoutMenuClick_ClosesMenuAndCallsLogout()
    {
        UseCountingHubFactory();
        var cut = Render<MainLayout>();
        var factory = (CountingHubConnectionFactory)Services.GetRequiredService<HubConnectionFactory>();
        LayoutType.GetField("_userMenuOpen", Priv)!.SetValue(cut.Instance, true);

        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();

        var method = LayoutType.GetMethod("OnLogoutMenuClick", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // After logout the navigation should go to /login
        Assert.Contains("login", nav.Uri);
        Assert.Equal(1, factory.StopAllCount);
    }

    private void UseCountingHubFactory() =>
        Services.AddSingleton<HubConnectionFactory>(services => new CountingHubConnectionFactory(
            services.GetRequiredService<IConfiguration>(),
            services.GetRequiredService<AuthStateProvider>()));

    // ── OnActiveOrgChanged ────────────────────────────────────────────────────

    [Fact]
    public async Task OnActiveOrgChanged_WithIntValue_CallsSetActive()
    {
        var cut = Render<MainLayout>();
        // Seed the available orgs so SetActiveAsync(42) can resolve a match and switch the active org.
        var orgs = Services.GetRequiredService<ActiveOrganizationService>();
        typeof(ActiveOrganizationService).GetProperty("Available")!
            .SetValue(orgs, new List<MyOrganizationDto>
            {
                new(42, "Acme", "acme", OrganizationRole.Owner)
            });

        // OnActiveOrgChanged is internal; accessible via InternalsVisibleTo
        await cut.InvokeAsync(async () => await cut.Instance.OnActiveOrgChanged(42));

        // The int branch dispatches to SetActiveAsync, which switches the active org to the matched id.
        Assert.Equal(42, orgs.Active?.Id);
    }

    [Fact]
    public async Task OnActiveOrgChanged_WithNonIntValue_ReturnsCompletedTask()
    {
        var cut = Render<MainLayout>();
        var orgs = Services.GetRequiredService<ActiveOrganizationService>();
        typeof(ActiveOrganizationService).GetProperty("Available")!
            .SetValue(orgs, new List<MyOrganizationDto>
            {
                new(42, "Acme", "acme", OrganizationRole.Owner)
            });

        await cut.InvokeAsync(async () => await cut.Instance.OnActiveOrgChanged("not-an-int"));

        // A non-int value short-circuits to Task.CompletedTask: no active org is selected.
        Assert.Null(orgs.Active);
    }

    // ── StaticResponseHandler helper ─────────────────────────────────────────

    /// <summary>Minimal HttpMessageHandler that always returns the given string body as 200 OK.</summary>
    private sealed class StaticResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
    }
}
