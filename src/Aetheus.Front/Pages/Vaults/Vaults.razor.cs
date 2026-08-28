// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Vaults;

// 0-a: thin page wrapper - the list, filtering and live refresh live in the shared VaultsList
// component (reused unfiltered here, scoped on project/server detail pages).
public partial class Vaults
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;

    protected override void OnInitialized() =>
        Breadcrumb.Set(new BreadcrumbItem(L["Vaults"]));
}
