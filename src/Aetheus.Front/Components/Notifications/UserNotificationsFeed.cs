// SPDX-License-Identifier: EUPL-1.2
using System.Net.Http;

namespace Aetheus.Front.Components.Notifications;

/// <summary>
/// R-115: the single source of the current user's notifications in the browser. The header bell, the
/// menu badge and the /notifications page all read <see cref="UnreadCount"/> from here and listen to
/// <see cref="OnChanged"/>, so they can never disagree. A singleton for the session, like
/// <c>PendingApprovalsService</c>.
///
/// Refresh: the backend pushes <c>NotificationsChanged</c> on <c>/hubs/user</c> for every delivery
/// recorded or read (R-182, wired in <c>UserNotificationService</c>), which reloads this feed at
/// once. The feed also reloads after every mark-read action here, and a slow <see cref="PollInterval"/>
/// catch-up remains only for pushes missed while the hub was disconnected.
/// </summary>
public sealed class UserNotificationsFeed : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    public const int LatestCount = 5;

    private readonly ApiClient _api;
    private readonly AuthStateProvider _auth;
    private readonly TimeProvider _time;
    private readonly ILogger<UserNotificationsFeed> _logger;
    /// <summary>R-461: pushes arriving within this window after the first one are served by one refresh.</summary>
    public const int PushCoalesceMilliseconds = 1000;

    private readonly object _gate = new();
    private readonly TrailingReloadCoalescer _pushRefresh = new(PushCoalesceMilliseconds);
    private ITimer? _poll;
    private Task _startup = Task.CompletedTask;

    public UserNotificationsFeed(ApiClient api, AuthStateProvider auth, TimeProvider time, ILogger<UserNotificationsFeed> logger)
    {
        _api = api;
        _auth = auth;
        _time = time;
        _logger = logger;
        _auth.OnAuthStateChanged += OnAuthStateChanged;
    }

    public int UnreadCount { get; private set; }

    /// <summary>The newest deliveries, for the bell's popover.</summary>
    public IReadOnlyList<NotificationDeliveryDto> Latest { get; private set; } = [];

    public event Action? OnChanged;

    /// <summary>
    /// Loads the count and starts the poll on the first call. Safe to call from every view: R-461, the
    /// bell and the menu entry both start the feed when the layout loads, and used to send the pair of
    /// requests twice; a later call now waits for the first load instead of sending its own.
    /// </summary>
    public async Task EnsureStartedAsync()
    {
        if (!_auth.IsAuthenticated) return;
        Task load;
        lock (_gate)
        {
            if (_poll is null)
            {
                _poll = _time.CreateTimer(_ => _ = RefreshAsync(), null, PollInterval, PollInterval);
                _startup = RefreshAsync();
            }
            load = _startup;
        }
        await load.ConfigureAwait(false);
    }

    /// <summary>
    /// R-461: a <c>NotificationsChanged</c> push. A burst of pushes (several deliveries recorded at once)
    /// is served by one refresh at the end of <see cref="PushCoalesceMilliseconds"/>, not one pair of
    /// requests per push. Returns at once, so the hub's message loop is never held.
    /// </summary>
    public void RefreshFromPush() => _ = _pushRefresh.RequestBestEffortAsync(RefreshAsync, CancellationToken.None);

    public async Task RefreshAsync()
    {
        if (!_auth.IsAuthenticated) return;
        try
        {
            var countTask = _api.Notifications.GetUnreadCountAsync();
            var latestTask = _api.Notifications.GetMineAsync(1, LatestCount);
            await Task.WhenAll(countTask, latestTask).ConfigureAwait(false);
            UnreadCount = await countTask.ConfigureAwait(false);
            Latest = (await latestTask.ConfigureAwait(false)).Items;
            OnChanged?.Invoke();
        }
        catch (HttpRequestException ex)
        {
            // Best effort like the other top-bar surfaces: the last known values stay.
            _logger.LogWarning(ex, "[UserNotifications] refresh failed");
        }
    }

    public async Task<bool> MarkReadAsync(int deliveryId)
    {
        var ok = await _api.Notifications.MarkReadAsync(deliveryId).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
        return ok;
    }

    /// <summary>Marks everything read; false when the server refused, the count then stays as it was.</summary>
    public async Task<bool> MarkAllReadAsync()
    {
        var changed = await _api.Notifications.MarkAllReadAsync().ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
        return changed is not null;
    }

    private void OnAuthStateChanged()
    {
        if (_auth.IsAuthenticated) return;
        lock (_gate)
        {
            _poll?.Dispose();
            _poll = null;
            _startup = Task.CompletedTask;
        }
        // A count loaded for one user must not survive into the next session in the same browser.
        UnreadCount = 0;
        Latest = [];
        OnChanged?.Invoke();
    }

    public void Dispose()
    {
        _auth.OnAuthStateChanged -= OnAuthStateChanged;
        lock (_gate)
        {
            _poll?.Dispose();
            _poll = null;
        }
    }
}
