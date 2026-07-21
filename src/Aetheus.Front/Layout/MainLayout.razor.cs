// SPDX-License-Identifier: EUPL-1.2
using System.Net.Http.Json;
using System.Text.Json;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace Aetheus.Front.Layout;

public partial class MainLayout : IDisposable
{
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private HelpService Help { get; set; } = default!;
    [Inject] private IConfiguration Configuration { get; set; } = default!;
    [Inject] private ILogger<MainLayout> Logger { get; set; } = default!;
    [Inject] private ActiveOrganizationService Orgs { get; set; } = default!;
    [Inject] private ListCacheService Cache { get; set; } = default!;
    [Inject] private IHttpClientFactory HttpFactory { get; set; } = default!;
    [Inject] private TaskTrackerService TaskTracker { get; set; } = default!;
    [Inject] private HubConnectionFactory? HubFactory { get; set; }
    [Inject] private IWebAssemblyHostEnvironment HostEnv { get; set; } = default!;
    [Inject] private NotifyHelper Notify { get; set; } = default!;

    private bool _sidebarExpanded = true;
    private bool _initialized;
    private bool _splashHidden;

    // Mobile drawer state (Astraia parity). Desktop-first defaults so the chrome never flashes a
    // collapsed rail before the JS viewport watcher reports in. On phones (<=768px) the sidebar
    // becomes a hidden overlay drawer; on desktop it stays the always-open rail.
    private bool _isMobile;
    private DotNetObjectReference<MainLayout>? _selfRef;

    /// <summary>Chantier I: the chrome renders only once the boot bootstrap has run to completion -
    /// auth resolved and, when authenticated, the parallel orgs + permissions load has finished
    /// (best-effort). Until then the splash stays up; if a load fails the app still shows (graceful
    /// degradation: disabled actions) rather than stranding the user on an endless splash.</summary>
    private bool _appReady;
    private bool AppReady => _appReady;
    private bool CanAccessCurrentRoute => RouteAccessPolicy.CanAccess(
        Nav.ToBaseRelativePath(Nav.Uri),
        Auth.IsAuthenticated,
        Auth.IsAdmin,
        Permissions);
    internal bool _darkMode = true;
    private string _currentLangLabel = "FR";

    // Dev-only top-bar banner: hidden in Production. Branch + Label come from the gitignored
    // wwwroot/appsettings.Development.json that ylaunch writes (so they are absent in prod);
    // the environment name is the live WASM host environment.
    private bool _isProduction = true;
    private string _envName = "Production";
    private string? _devBranch;
    private string? _devLabel;
    private ErrorBoundary? _errorBoundary;
    private bool _userMenuOpen;
    private string _version = "dev";
    internal bool _newVersionAvailable;
    private CancellationTokenSource? _versionCheckCts;
    private Microsoft.AspNetCore.SignalR.Client.HubConnectionState _signalRState = Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Disconnected;

    // Connection-lost dialog state (ported from Astraia). The dialog appears only after a short grace
    // period so a quick blip or a page reload does not flash it; it closes on its own once the hub
    // reconnects. The countdown mirrors the automatic-reconnect schedule surfaced by the tracker.
    private bool _showOfflineDialog;
    private int _reconnectCountdown;
    private bool _manualReconnecting;
    private bool _wasConnectedOnce;
    // Set the moment the boot bootstrap kicks off TaskTracker.StartAsync, regardless of whether that
    // first attempt actually reaches Connected. Without this, a backend that is down at first load
    // never sets _wasConnectedOnce (StartAsync swallows its own failure) so HandleConnectionStateChanged
    // never surfaces the connection-lost dialog and the user is stuck with no feedback and no recovery
    // path until a manual F5 - see TaskTrackerService's own initial-connect retry loop for the other
    // half of the fix (arming the actual retries).
    private bool _connectAttempted;
    private bool _disposed;
    private CancellationTokenSource? _offlineDelayCts;
    private CancellationTokenSource? _countdownCts;

    protected override async Task OnInitializedAsync()
    {
        _isProduction = HostEnv.IsProduction();
        _envName = HostEnv.Environment;
        _devBranch = Configuration["DevBanner:Branch"];
        _devLabel = Configuration["DevBanner:Label"];

        await Auth.InitializeAsync();
        _initialized = true;
        Auth.OnAuthStateChanged += OnAuthStateChanged;
        Auth.OnNeedsLogin += OnNeedsLogin;
        Nav.LocationChanged += OnLocationChanged;
        Breadcrumb.OnChanged += OnBreadcrumbChanged;
        TaskTracker.OnConnectionStateChanged += OnSignalRStateChanged;
        TaskTracker.OnReconnectAttempt += OnReconnectAttempt;
        _signalRState = TaskTracker.ConnectionState;
        // If the tracker already connected before this layout mounted, remember it so a later drop
        // surfaces the connection-lost dialog (it only shows after having been connected at least once).
        _wasConnectedOnce = _signalRState == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected;

        // Permissions and organizations are independent - load them in parallel.
        // Sequential awaits added a full prod round-trip (~280ms) to every page load.
        var bootstrap = new List<Task>(2);
        if (Auth.IsAuthenticated && !Permissions.IsLoaded)
            bootstrap.Add(LoadPermissionsAsync());
        if (Auth.IsAuthenticated)
        {
            Orgs.Changed += OnOrgsChanged;
            if (!Orgs.IsLoaded)
                bootstrap.Add(LoadOrganizationsAsync());
        }
        if (bootstrap.Count > 0)
            await Task.WhenAll(bootstrap);

        // Own the connectivity lifecycle exactly like Astraia's MainLayout: start the global hub HERE
        // and await it, then record the connected baseline deterministically - instead of racing a
        // child widget's start and hoping its connect event arrives before a later drop. StartAsync is
        // idempotent, so the TaskTrackerWidget calling it too is a no-op.
        if (Auth.IsAuthenticated)
        {
            _connectAttempted = true;
            try { await TaskTracker.StartAsync(); }
            catch (Exception ex) { Logger.LogWarning(ex, "[MainLayout] TaskTracker start failed"); }
            _signalRState = TaskTracker.ConnectionState;
            _wasConnectedOnce = _signalRState == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected;
        }

        try
        {
            var theme = await JS.InvokeAsync<string?>("localStorage.getItem", StorageKeys.Theme);
            _darkMode = theme != "light";
        }
        catch (Exception ex)
        {
            // Prerendering guard - localStorage not available on server
            Logger.LogWarning(ex, "[MainLayout] localStorage read failed");
        }

        _currentLangLabel = System.Globalization.CultureInfo.CurrentUICulture.Name
            .StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? "FR" : "EN";

        RedirectIfUnauthenticated();
        _appReady = true;
    }

    private async Task LoadPermissionsAsync()
    {
        try
        {
            var summary = await Api.GetMyPermissionsAsync();
            if (summary is not null)
                Permissions.SetPermissions(summary.EffectivePermissions, Auth.IsAdmin);
        }
        catch (Exception ex)
        {
            // Permission loading failure should not break the app
            Logger.LogWarning(ex, "[MainLayout] Permissions load failed");
        }
    }

    private async Task LoadOrganizationsAsync()
    {
        try { await Orgs.RefreshAsync(); }
        catch (Exception ex) { Logger.LogWarning(ex, "[MainLayout] Organizations load failed"); }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var css = _darkMode ? RadzenAssetUrls.DarkTheme : RadzenAssetUrls.LightTheme;
            await JS.InvokeVoidAsync("Aetheus.setTheme", css);
            _version = Configuration["App:Version"] ?? "dev";
            await JS.InvokeVoidAsync("Aetheus.lockTitle", $"Aetheus v{_version}");
            // Worktree distinction: when a dev-banner label is set (e.g. "P4"), prefix every tab
            // title with it. Absent in production, so the title stays unchanged there.
            if (!string.IsNullOrWhiteSpace(_devLabel))
                await JS.InvokeVoidAsync("Aetheus.setTitlePrefix", _devLabel);
            await JS.InvokeVoidAsync("Aetheus.initFormShortcuts");
            // Start the mobile-drawer viewport watcher: it reports the current <=768px state now and on
            // every crossing, driving _isMobile / _sidebarExpanded (see OnViewportChanged).
            _selfRef = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("Aetheus.watchViewport", _selfRef);
            StartVersionCheckLoop();
        }

        // Boot splash handoff: the splash is a single continuous node in index.html (outside #app),
        // so there is no animation-restart flash when Blazor takes over. Fade it out once the chrome
        // is actually on screen (AppReady). Runs exactly once.
        if (AppReady && !_splashHidden)
        {
            _splashHidden = true;
            await JS.InvokeVoidAsync("Aetheus.hideSplash");
        }
    }

    private void StartVersionCheckLoop()
    {
        _versionCheckCts = new CancellationTokenSource();
        _ = VersionCheckLoopAsync(_versionCheckCts.Token);
    }

    private async Task VersionCheckLoopAsync(CancellationToken ct)
    {
        using var http = HttpFactory.CreateClient();
        http.BaseAddress = new Uri(Nav.BaseUri);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), ct);
                if (await CheckForNewVersionAsync(http, ct))
                    return;
            }
            catch (OperationCanceledException) { return; }
            catch { /* network error - retry next cycle */ }
        }
    }

    private async Task<bool> CheckForNewVersionAsync(HttpClient http, CancellationToken ct)
    {
        // Cache-bust so a CDN/browser cache cannot mask a new deployment.
        var json = await http.GetStringAsync($"appsettings.json?_={Environment.TickCount64}", ct);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("App", out var appSection)
            && appSection.TryGetProperty("Version", out var versionProp))
        {
            var remoteVersion = versionProp.GetString();
            if (!string.IsNullOrEmpty(remoteVersion) && remoteVersion != _version)
            {
                _newVersionAvailable = true;
                // APR9: a backend redeploy means the cached OpenAPI endpoints are stale - drop them so the
                // API Reference page transparently refetches the new spec on its next visit, automatically,
                // without waiting for a full app reload or a manual refresh button.
                Cache.Invalidate(ListCacheService.ApiReferenceKey);
                await InvokeAsync(StateHasChanged);
                return true;
            }
        }
        return false;
    }

    internal void OnReloadClick()
    {
        Nav.NavigateTo(Nav.Uri, forceLoad: true);
    }

    private void OnAuthStateChanged()
    {
        RedirectIfUnauthenticated();
        StateHasChanged();
    }

    // F-41: single point of truth for handling 401s. Bouncing all unauth events through here
    // (instead of letting each HTTP handler call NavigateTo) avoids redirect loops when several
    // requests fail in parallel.
    private void OnNeedsLogin()
    {
        InvokeAsync(() =>
        {
            // A rejected token logs AuthStateProvider out before raising this event. Clear every
            // session-scoped projection here too, otherwise a second account can briefly receive
            // the previous account's cached rows while its first requests revalidate.
            Permissions.Clear();
            Breadcrumb.Clear();
            Cache.Clear();

            var uri = Nav.ToBaseRelativePath(Nav.Uri);
            if (!uri.StartsWith("login", StringComparison.OrdinalIgnoreCase))
            {
                // S5TK: the redirect used to be silent (a flash of the empty dashboard, then /login).
                // Tell the user why they were bounced so an expired session doesn't read as a glitch.
                Notify.Warning("SessionExpired", "SessionExpiredDetail");
                Nav.NavigateTo("/login");
            }
        });
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        _errorBoundary?.Recover();
        _userMenuOpen = false;
        // Mobile drawer: tapping a menu link navigates, so close the overlay so it doesn't cover the
        // page the user just opened (Astraia parity). No-op on desktop where the rail stays open.
        if (_isMobile)
            _sidebarExpanded = false;
        RedirectIfUnauthenticated();
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>Called by Aetheus.watchViewport (layout.js) on boot and on every 768px crossing.
    /// Collapses the sidebar to the hidden overlay drawer on phones and restores the always-open
    /// rail on desktop, so the same RadzenSidebar serves both without changing desktop behavior.</summary>
    [JSInvokable]
    public void OnViewportChanged(bool mobile)
    {
        _isMobile = mobile;
        _sidebarExpanded = !mobile;
        _ = InvokeAsync(StateHasChanged);
    }

    private void RedirectIfUnauthenticated()
    {
        var uri = Nav.ToBaseRelativePath(Nav.Uri);
        if (!Auth.IsAuthenticated)
        {
            if (!uri.StartsWith("login", StringComparison.OrdinalIgnoreCase))
                Nav.NavigateTo("/login");
            return;
        }

        // Authenticated but flagged for a forced password change: gate ALL navigation onto the
        // mandatory change-password screen until a fresh token (without the claim) is issued.
        if (Auth.MustChangePassword
            && !uri.StartsWith("account/change-password", StringComparison.OrdinalIgnoreCase))
            Nav.NavigateTo("/account/change-password");
    }

    internal async Task ToggleDarkMode()
    {
        _userMenuOpen = false;
        _darkMode = !_darkMode;
        var theme = _darkMode ? "dark" : "light";
        await JS.InvokeVoidAsync("localStorage.setItem", StorageKeys.Theme, theme);
        var css = _darkMode ? RadzenAssetUrls.DarkTheme : RadzenAssetUrls.LightTheme;
        await JS.InvokeVoidAsync("Aetheus.setTheme", css);
    }

    private async Task ToggleLanguage()
    {
        _userMenuOpen = false;
        var current = System.Globalization.CultureInfo.CurrentUICulture.Name;
        var next = current.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? "en" : "fr-FR";
        await JS.InvokeVoidAsync("localStorage.setItem", StorageKeys.Lang, next);
        await JS.InvokeVoidAsync("Aetheus.setLang", next);
        Nav.NavigateTo(Nav.Uri, forceLoad: true);
    }

    private void ToggleUserMenu()
    {
        _userMenuOpen = !_userMenuOpen;
        if (_userMenuOpen)
            _ = FocusUserMenuAsync();
    }

    private async Task FocusUserMenuAsync()
    {
        await Task.Yield();
        await JS.InvokeVoidAsync("Aetheus.trapFocus", ".user-menu-card");
    }

    private void HandleUserMenuKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            _userMenuOpen = false;
    }

    // A11y: user-menu items are <div role="button">; Enter/Space must activate them
    // like a native button does, otherwise they are focusable but not keyboard-operable.
    private static bool IsActivationKey(KeyboardEventArgs e) => e.Key is "Enter" or " ";

    private async Task ActivateOnKey(KeyboardEventArgs e, Func<Task> action)
    {
        if (IsActivationKey(e))
            await action();
    }

    private void ActivateOnKey(KeyboardEventArgs e, Action action)
    {
        if (IsActivationKey(e))
            action();
    }

    private void CloseUserMenu()
    {
        _userMenuOpen = false;
    }

    private void OnSettingsMenuClick()
    {
        _userMenuOpen = false;
        Nav.NavigateTo("/settings");
    }

    private async Task OnLogoutMenuClick()
    {
        _userMenuOpen = false;
        await OnLogout();
    }

    internal void RecoverError()
    {
        _errorBoundary?.Recover();
    }

    private void OnBreadcrumbChanged() => InvokeAsync(StateHasChanged);

    internal async Task OnLogout()
    {
        Permissions.Clear();
        // Defence in depth: drop any breadcrumb trail so the next user can't glimpse the prior
        // session's navigation context before LocationChanged clears it on the /login redirect.
        Breadcrumb.Clear();
        // Drop the session list cache (e.g. the cached OpenAPI spec for the API Reference page) so the
        // next user never reads stale data carried across a sign-out.
        Cache.Clear();
        // Stop every page/global SignalR connection while the token is still valid. Otherwise a
        // reconnect can race the token removal and issue an expected-but-noisy 401 during logout.
        if (HubFactory is not null)
            await HubFactory.StopAllAsync();
        await Auth.LogoutAsync();
        Nav.NavigateTo("/login");
    }

    internal void NavigateToHelp()
    {
        var relative = Nav.ToBaseRelativePath(Nav.Uri);
        var pageKey = Help.ResolvePageKey(relative);
        Nav.NavigateTo(pageKey is not null ? $"/help/{pageKey}" : "/help");
    }

    private void OnSignalRStateChanged()
    {
        _signalRState = TaskTracker.ConnectionState;
        var connected = _signalRState == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected;
        HandleConnectionStateChanged(connected);
    }

    // Ported from Astraia MainLayout.OnConnectionStateChanged. On reconnect, tear down the countdown
    // and close the dialog; on drop, show it only after a 2s grace so a reload/blip does not flash it.
    private void HandleConnectionStateChanged(bool connected)
    {
        _ = InvokeAsync(async () =>
        {
            if (connected)
            {
                _offlineDelayCts?.Cancel();
                _offlineDelayCts?.Dispose();
                _offlineDelayCts = null;
                _countdownCts?.Cancel();
                _countdownCts?.Dispose();
                _countdownCts = null;
                _reconnectCountdown = 0;

                if (_showOfflineDialog)
                {
                    // Connection restored - close the dialog without reloading the page.
                    _showOfflineDialog = false;
                    _manualReconnecting = false;
                }

                _wasConnectedOnce = true;
                StateHasChanged();
                return;
            }

            // Disconnected - show the dialog only after a 2s grace period (avoids flashing on reload).
            // Also arm this path on a first-ever failed connect attempt (_connectAttempted), not just
            // after a prior successful one (_wasConnectedOnce) - otherwise a backend that is down at
            // login never shows the dialog at all (see the _connectAttempted field comment).
            if (_wasConnectedOnce || _connectAttempted)
            {
                _offlineDelayCts?.Cancel();
                _offlineDelayCts?.Dispose();
                _offlineDelayCts = new CancellationTokenSource();
                var token = _offlineDelayCts.Token;
                try
                {
                    await Task.Delay(2000, token);
                    if (!token.IsCancellationRequested && !_disposed)
                    {
                        _showOfflineDialog = true;
                        StateHasChanged();
                    }
                }
                catch (OperationCanceledException)
                {
                    // Reconnected before the delay elapsed - nothing to show.
                }
            }
        });
    }

    // Drives the "reconnect in N" countdown from each scheduled automatic-reconnect attempt.
    private void OnReconnectAttempt(int delaySeconds)
    {
        _ = InvokeAsync(async () =>
        {
            _countdownCts?.Cancel();
            _countdownCts?.Dispose();
            _countdownCts = new CancellationTokenSource();
            var token = _countdownCts.Token;
            _reconnectCountdown = delaySeconds;
            StateHasChanged();
            try
            {
                while (_reconnectCountdown > 0 && !token.IsCancellationRequested && !_disposed)
                {
                    await Task.Delay(1000, token);
                    _reconnectCountdown--;
                    StateHasChanged();
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    private async Task OnManualReconnect()
    {
        _countdownCts?.Cancel();
        _countdownCts?.Dispose();
        _countdownCts = null;
        _reconnectCountdown = 0;
        _manualReconnecting = true;
        StateHasChanged();

        await TaskTracker.ReconnectAsync();
        _manualReconnecting = false;

        if (TaskTracker.ConnectionState == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected)
        {
            _showOfflineDialog = false;
        }
        // Failed - TaskTracker.StartAsync (invoked by ReconnectAsync) now schedules its own
        // initial-connect retry loop and raises OnReconnectAttempt for every attempt (see
        // TaskTrackerService.InitialRetryLoopAsync), which re-arms the visible countdown through the
        // OnReconnectAttempt handler below. No local fallback countdown needed here, and the hub the
        // failed attempt left behind is not abandoned un-retried.
        StateHasChanged();
    }

    public void Dispose()
    {
        _disposed = true;
        Auth.OnAuthStateChanged -= OnAuthStateChanged;
        Auth.OnNeedsLogin -= OnNeedsLogin;
        Nav.LocationChanged -= OnLocationChanged;
        Breadcrumb.OnChanged -= OnBreadcrumbChanged;
        Orgs.Changed -= OnOrgsChanged;
        if (TaskTracker is not null)
        {
            TaskTracker.OnConnectionStateChanged -= OnSignalRStateChanged;
            TaskTracker.OnReconnectAttempt -= OnReconnectAttempt;
        }
        _offlineDelayCts?.Cancel();
        _offlineDelayCts?.Dispose();
        _countdownCts?.Cancel();
        _countdownCts?.Dispose();
        _versionCheckCts?.Cancel();
        _versionCheckCts?.Dispose();
        _ = JS.InvokeVoidAsync("Aetheus.disposeViewportWatcher");
        _selfRef?.Dispose();
    }

    private void OnOrgsChanged() => InvokeAsync(StateHasChanged);

    internal Task OnActiveOrgChanged(object value)
    {
        // ORG6: the org picker lives inside the user submenu - close it on selection so the menu
        // doesn't linger open through the Orgs.SetActiveAsync reload.
        _userMenuOpen = false;
        if (value is not int id) return Task.CompletedTask;
        // Org-scoped lists (and any other session-cached data) belong to the previous organization -
        // drop the cache so the switch re-fetches under the new org instead of serving stale entries.
        Cache.Clear();
        return Orgs.SetActiveAsync(id);
    }
}
