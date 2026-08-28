// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Settings;

public partial class Administration
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;

    protected override void OnInitialized()
    {
        if (!Auth.IsAdmin)
        {
            Nav.NavigateTo("/");
            return;
        }

        Breadcrumb.Set(new BreadcrumbItem(L["Administration"]));
    }
}
