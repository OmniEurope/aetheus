// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;

namespace Aetheus.Front.Layout;

public partial class MainLayout : IAsyncDisposable
{
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private TimeProvider TimeProvider { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private HelpService Help { get; set; } = default!;
    [Inject] private IConfiguration Configuration { get; set; } = default!;
    [Inject] private ILogger<MainLayout> Logger { get; set; } = default!;
    [Inject] private ActiveOrganizationService Orgs { get; set; } = default!;
    [Inject] private ListCacheService Cache { get; set; } = default!;
    [Inject] private IHttpClientFactory HttpFactory { get; set; } = default!;
    [Inject] private TaskTrackerService TaskTracker { get; set; } = default!;
    [Inject] private RealtimeSessionLifecycle RealtimeSession { get; set; } = default!;
    [Inject] private IWebAssemblyHostEnvironment HostEnv { get; set; } = default!;
    [Inject] private NotifyHelper Notify { get; set; } = default!;

    private bool _sidebarExpanded;
    private bool _initialized;
    private bool _splashHidden;

    private bool _isMobile;
    private bool _viewportKnown;
    internal bool IsSidebarExpanded => _sidebarExpanded;
    internal bool IsMobileViewport => _isMobile;
    internal bool IsViewportKnown => _viewportKnown;
    private bool _desktopSidebarExpanded = true;
    private DotNetObjectReference<MainLayout>? _selfRef;

    private bool _appReady;
    private bool AppReady => _appReady;
    private bool _authenticatedSessionReady;
    private bool AuthenticatedChromeReady => Auth.IsAuthenticated && _authenticatedSessionReady;
    private bool CanAccessCurrentRoute => RouteAccessPolicy.CanAccess(
        Nav.ToBaseRelativePath(Nav.Uri),
        Auth.IsAuthenticated,
        Auth.IsAdmin,
        Permissions);
    internal bool _darkMode = true;
    private string _currentLangLabel = "FR";

    private bool _isProduction = true;
    private string _envName = "Production";
    private string? _devBranch;
    private string? _devLabel;
    private LoggedErrorBoundary? _errorBoundary;
    private bool _userMenuOpen;
    private string _version = "dev";
    internal bool _newVersionAvailable;
    private ApplicationVersionMonitor? _versionMonitor;
    private Microsoft.AspNetCore.SignalR.Client.HubConnectionState _signalRState = Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Disconnected;
    private bool? _backendLive;
    internal bool BackendConnected =>
        _signalRState == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected
        || _backendLive == true;

    private bool _showOfflineDialog;
    private int _reconnectCountdown;
    private bool _manualReconnecting;
    private bool _wasConnectedOnce;
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

        await Auth.InitializeAsync();
        _initialized = true;
        Auth.OnAuthStateChanged += OnAuthStateChanged;
        Auth.OnNeedsLogin += OnNeedsLogin;
        Nav.LocationChanged += OnLocationChanged;
        TaskTracker.OnConnectionStateChanged += OnSignalRStateChanged;
        TaskTracker.OnReconnectAttempt += OnReconnectAttempt;
        _signalRState = TaskTracker.ConnectionState;
        _wasConnectedOnce = _signalRState == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected;

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

        if (Auth.IsAuthenticated)
        {
            _connectAttempted = true;
            try { await TaskTracker.StartAsync(); }
            catch (Exception ex) { Logger.LogWarning(ex, "[MainLayout] TaskTracker start failed"); }
            _signalRState = TaskTracker.ConnectionState;
            _wasConnectedOnce = _signalRState == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected;
            if (_wasConnectedOnce)
                _backendLive = true;
            else
                _backendLive = await Api.Auth.IsBackendLiveAsync();
        }
        _authenticatedSessionReady = Auth.IsAuthenticated;

        _currentLangLabel = System.Globalization.CultureInfo.CurrentUICulture.Name
            .StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? "FR" : "EN";

        RedirectIfUnauthenticated();
        _appReady = true;
    }

    private Task LoadPermissionsAsync() =>
        MainLayoutBootstrapLoader.LoadPermissionsAsync(Api, Permissions, Auth, Logger);

    private Task LoadOrganizationsAsync() =>
        MainLayoutBootstrapLoader.LoadOrganizationsAsync(Orgs, Logger);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var css = _darkMode ? RadzenAssetUrls.DarkTheme : RadzenAssetUrls.LightTheme;
            await JS.InvokeVoidAsync("Aetheus.setTheme", css);
            _version = Configuration["App:Version"] ?? "dev";
            await JS.InvokeVoidAsync("Aetheus.lockTitle", $"Aetheus v{_version}");
            if (!string.IsNullOrWhiteSpace(_devLabel))
                await JS.InvokeVoidAsync("Aetheus.setTitlePrefix", _devLabel);
            await JS.InvokeVoidAsync("Aetheus.initFormShortcuts");
            _selfRef = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("Aetheus.watchViewport", _selfRef);
            _versionMonitor = new ApplicationVersionMonitor(
                HttpFactory, new Uri(Nav.BaseUri), _version, OnNewVersionAvailableAsync);
            _versionMonitor.Start();
        }

        if (AppReady && _viewportKnown && !_splashHidden)
        {
            _splashHidden = true;
            await JS.InvokeVoidAsync("Aetheus.hideSplash");
        }
    }

    private Task<bool> CheckForNewVersionAsync(HttpClient http, CancellationToken ct) =>
        ApplicationVersionMonitor.CheckAsync(http, _version, OnNewVersionAvailableAsync, ct);

    private Task OnNewVersionAvailableAsync()
    {
        _newVersionAvailable = true;
        Cache.Invalidate(ListCacheService.ApiReferenceKey);
        return InvokeAsync(StateHasChanged);
    }

    internal void OnReloadClick()
    {
        Nav.NavigateTo(Nav.Uri, forceLoad: true);
    }

    private void OnAuthStateChanged()
    {
        if (!Auth.IsAuthenticated)
        {
            _authenticatedSessionReady = false;
            ClearSessionState();
            ResetConnectionLostState();
        }
        else if (!_authenticatedSessionReady)
        {
            _ = InvokeAsync(InitializeAuthenticatedSessionAsync);
        }
        RedirectIfUnauthenticated();
        StateHasChanged();
    }

    private async Task InitializeAuthenticatedSessionAsync()
    {
        if (!Auth.IsAuthenticated || _authenticatedSessionReady) return;
        try
        {
            Orgs.Changed -= OnOrgsChanged;
            Orgs.Changed += OnOrgsChanged;
            _connectAttempted = true;
            var result = await MainLayoutBootstrapLoader.StartAuthenticatedSessionAsync(
                Api, Permissions, Auth, Orgs, TaskTracker, Logger);
            _signalRState = result.SignalRState;
            _wasConnectedOnce = result.WasConnected;
            _backendLive = result.BackendLive;
            _authenticatedSessionReady = Auth.IsAuthenticated;
        }
        finally
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    // F-41: single point of truth for handling 401s. Bouncing all unauth events through here
    // (instead of letting each HTTP handler call NavigateTo) avoids redirect loops when several
    // requests fail in parallel.
    private void OnNeedsLogin()
    {
        _ = InvokeAsync(async () =>
        {
            await RealtimeSession.StopAsync();
            // A rejected token logs AuthStateProvider out before raising this event. Clear every
            // session-scoped projection here too, otherwise a second account can briefly receive
            // the previous account's cached rows while its first requests revalidate.
            ClearSessionState();
            ResetConnectionLostState();

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

    /// <summary>Called by Aetheus.watchViewport on boot and on every 1024px crossing. The initial
    /// callback is part of the splash handoff, preventing an incorrect sidebar frame from becoming
    /// visible during a cold load.</summary>
    [JSInvokable]
    public async Task OnViewportChanged(bool mobile)
    {
        var viewportModeChanged = _viewportKnown && _isMobile != mobile;
        _isMobile = mobile;
        if (!_viewportKnown || viewportModeChanged)
            _sidebarExpanded = mobile ? false : _desktopSidebarExpanded;
        _viewportKnown = true;
        await InvokeAsync(StateHasChanged);
    }

    private void ToggleSidebar()
    {
        _sidebarExpanded = !_sidebarExpanded;
        if (!_isMobile)
            _desktopSidebarExpanded = _sidebarExpanded;
    }

    private void OnSidebarNavigationRequested()
    {
        if (_isMobile)
            _sidebarExpanded = false;
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

    internal async Task OnLogout()
    {
        ClearSessionState();
        // Stop and reset every realtime owner while the token is still valid. This prevents both a
        // reconnect race during logout and per-user state leaking into a same-runtime re-login.
        await RealtimeSession.StopAsync();
        ResetConnectionLostState();
        await Auth.LogoutAsync();
        Nav.NavigateTo("/login");
    }

    private void ClearSessionState()
    {
        Permissions.Clear();
        Cache.Clear();
        Orgs.Clear();
    }
    internal void NavigateToHelp()
    {
        var relative = Nav.ToBaseRelativePath(Nav.Uri);
        var pageKey = Help.ResolvePageKey(relative);
        Nav.NavigateTo(pageKey is not null ? $"/help/{pageKey}" : "/help");
    }

    private void ResetConnectionLostState()
    {
        _offlineDelayCts?.Cancel();
        _offlineDelayCts?.Dispose();
        _offlineDelayCts = null;
        _countdownCts?.Cancel();
        _countdownCts?.Dispose();
        _countdownCts = null;
        _signalRState = Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Disconnected;
        _backendLive = null;
        _showOfflineDialog = false;
        _reconnectCountdown = 0;
        _manualReconnecting = false;
        _wasConnectedOnce = false;
        _connectAttempted = false;
    }

    private void OnSignalRStateChanged()
    {
        _signalRState = TaskTracker.ConnectionState;
        var connected = _signalRState == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected;
        if (connected)
            _backendLive = true;
        else
            _ = RefreshBackendLivenessAsync();
        HandleConnectionStateChanged(connected);
    }

    private async Task<bool> RefreshBackendLivenessAsync(CancellationToken cancellationToken = default)
    {
        var live = await Api.Auth.IsBackendLiveAsync(cancellationToken);
        if (_disposed || cancellationToken.IsCancellationRequested) return live;
        await InvokeAsync(() =>
        {
            _backendLive = live;
            if (live)
                _showOfflineDialog = false;
            StateHasChanged();
        });
        return live;
    }

    // On reconnect, tear down the countdown and close the dialog. On a drop, wait briefly before
    // declaring the backend unavailable so quick SignalR interruptions never flash a false alert.
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
                    _showOfflineDialog = false;
                    _manualReconnecting = false;
                }

                _wasConnectedOnce = true;
                StateHasChanged();
                return;
            }

            if (_wasConnectedOnce || _connectAttempted)
            {
                _offlineDelayCts?.Cancel();
                _offlineDelayCts?.Dispose();
                _offlineDelayCts = new CancellationTokenSource();
                var token = _offlineDelayCts.Token;
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), TimeProvider, token);
                    if (!token.IsCancellationRequested && !_disposed)
                    {
                        if (await RefreshBackendLivenessAsync(token))
                            return;
                        _showOfflineDialog = true;
                        StateHasChanged();
                    }
                }
                catch (OperationCanceledException)
                {
                    // Reconnected before the delay elapsed.
                }
            }
        });
    }

    private void OnReconnectAttempt(int delaySeconds)
    {
        _ = RefreshBackendLivenessAsync();
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
                    await Task.Delay(TimeSpan.FromSeconds(1), TimeProvider, token);
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
            _showOfflineDialog = false;

        // TaskTracker schedules subsequent attempts and drives the countdown through
        // OnReconnectAttempt when this immediate attempt cannot reconnect.
        StateHasChanged();
    }

    private void DisposeManagedState()
    {
        if (_disposed) return;
        _disposed = true;
        Auth.OnAuthStateChanged -= OnAuthStateChanged;
        Auth.OnNeedsLogin -= OnNeedsLogin;
        Nav.LocationChanged -= OnLocationChanged;
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
        _versionMonitor?.Dispose();
    }

    public void Dispose()
    {
        DisposeManagedState();
        _selfRef?.Dispose();
        _selfRef = null;
    }

    public async ValueTask DisposeAsync()
    {
        DisposeManagedState();
        try
        {
            await JS.InvokeVoidAsync("Aetheus.disposeViewportWatcher");
        }
        catch (JSDisconnectedException)
        {
            // The browser runtime is already gone; there is no watcher left to detach.
        }
        finally
        {
            _selfRef?.Dispose();
            _selfRef = null;
        }
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
