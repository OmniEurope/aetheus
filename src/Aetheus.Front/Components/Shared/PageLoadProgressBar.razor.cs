// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components.Routing;

namespace Aetheus.Front.Components.Shared;

public partial class PageLoadProgressBar : IDisposable
{
    [Inject] private PageLoadActivity Activity { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    protected override void OnInitialized()
    {
        Nav.LocationChanged += OnLocationChanged;
        Activity.Changed += OnActivityChanged;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => Activity.Begin();

    private void OnActivityChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Nav.LocationChanged -= OnLocationChanged;
        Activity.Changed -= OnActivityChanged;
    }
}
