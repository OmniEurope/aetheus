// SPDX-License-Identifier: EUPL-1.2

using Aetheus.Front.Layout;

namespace Aetheus.Front.Shared;

public partial class BrowserBackButton
{
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;

    [Parameter] public string? Text { get; set; }
    [Parameter] public string? Title { get; set; }
    [Parameter] public ButtonSize Size { get; set; } = ButtonSize.Medium;

    private async Task GoBackAsync()
    {
        if (Breadcrumb.ParentHref is { } parentHref)
        {
            Nav.NavigateTo(parentHref);
            return;
        }

        await Js.InvokeVoidAsync("Aetheus.goBack");
    }
}
