// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Tasks;

/// <summary>
/// Global tasks page: a thin host around the shared <see cref="TaskListView"/> (no server scope).
/// The per-server tasks section hosts the same component with a ServerId, so both views share an
/// identical feature set.
/// </summary>
public partial class Tasks
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;

    protected override void OnInitialized()
        => Breadcrumb.Set(new BreadcrumbItem(L["Tasks"]));
}
