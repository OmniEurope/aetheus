// SPDX-License-Identifier: EUPL-1.2
using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Components.Notifications;

public partial class NotificationsNavItem : IDisposable
{
    [Inject] private UserNotificationsFeed Feed { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private static readonly RenderFragment BellIcon = builder =>
    {
        builder.OpenComponent<OmniIcon>(0);
        builder.AddComponentParameter(1, nameof(OmniIcon.Name), OmniIconName.Bell);
        builder.CloseComponent();
    };

    protected override async Task OnInitializedAsync()
    {
        Feed.OnChanged += OnFeedChanged;
        await Feed.EnsureStartedAsync();
    }

    private void OnFeedChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => Feed.OnChanged -= OnFeedChanged;
}
