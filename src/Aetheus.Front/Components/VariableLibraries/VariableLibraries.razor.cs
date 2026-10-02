// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.VariableLibraries;

// 0-a: thin page wrapper - the list, filtering and live refresh live in the shared
// VariableLibrariesList component (reused unfiltered here, scoped on project/server detail pages).
public partial class VariableLibraries
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;

    protected override void OnInitialized() =>
        Breadcrumb.Set(new BreadcrumbItem(L["VariableLibraries"]));
}
