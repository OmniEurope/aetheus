// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Releases;

// 0-a: thin page wrapper - the list, filtering, actions and live refresh all live in the shared
// ReleasesList component (reused unfiltered here, and scoped on project/server detail pages).
public partial class Releases
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;

    protected override void OnInitialized() =>
        Breadcrumb.Set(new BreadcrumbItem(L["Releases"]));
}
