// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Environments;

// 0-a: thin page wrapper - the list, filtering, duplicate action and live refresh live in the shared
// EnvironmentsList component (reused unfiltered here, scoped on project detail pages).
public partial class Environments
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;

    protected override void OnInitialized() =>
        Breadcrumb.Set(new BreadcrumbItem(L["Environments"]));
}
