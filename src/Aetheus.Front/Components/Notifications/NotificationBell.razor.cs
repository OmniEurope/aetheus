// SPDX-License-Identifier: EUPL-1.2
using System.Net.Http;

namespace Aetheus.Front.Components.Notifications;

public partial class NotificationBell : IDisposable
{
    [Inject] private UserNotificationsFeed Feed { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private ApiClient Api { get; set; } = default!;

    private bool _popoverOpen;
    private bool _busy;
    private bool _followsNothing;
    private bool _followingAll;

    protected override async Task OnInitializedAsync()
    {
        Feed.OnChanged += OnFeedChanged;
        await Feed.EnsureStartedAsync();
    }

    private void OnFeedChanged() => _ = InvokeAsync(StateHasChanged);


    /// <summary>
    /// Recette R2-034: each opening reads whether the user follows any project, because a user who
    /// follows none receives nothing and the panel must say why. Read at opening only: following a
    /// project happens elsewhere (its page, the preferences), and the next opening sees it.
    /// </summary>
    private async Task OnOpenChangedAsync(bool open)
    {
        _popoverOpen = open;
        if (!open) return;
        try
        {
            _followsNothing = (await Api.Notifications.GetSubscriptionsAsync()).Count == 0;
        }
        catch (HttpRequestException)
        {
            // Unknown: no hint rather than a wrong one.
            _followsNothing = false;
        }
    }

    private async Task FollowAllAsync()
    {
        if (_followingAll) return;
        _followingAll = true;
        try
        {
            var subscriptions = await Api.Notifications.FollowAllProjectsAsync();
            if (subscriptions is null)
            {
                Toast.Error("Error", "OperationFailed");
                return;
            }
            _followsNothing = subscriptions.Count == 0;
            Toast.Success("Saved", "NotificationFollowedAllProjects", subscriptions.Count);
        }
        finally
        {
            _followingAll = false;
        }
    }

    private void GoTo(string href)
    {
        _popoverOpen = false;
        Nav.NavigateTo(href);
    }

    private async Task MarkAllReadAsync()
    {
        _busy = true;
        try
        {
            // Recette R-188: success needs no toast, the count and the row icons change in the open
            // panel; the toast covered that very panel. A failure still says so.
            if (!await Feed.MarkAllReadAsync())
                Toast.Error("Error", "OperationFailed");
        }
        finally
        {
            _busy = false;
        }
    }

    public void Dispose() => Feed.OnChanged -= OnFeedChanged;
}
