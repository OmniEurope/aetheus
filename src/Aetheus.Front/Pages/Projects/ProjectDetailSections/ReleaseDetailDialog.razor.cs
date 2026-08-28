// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

public partial class ReleaseDetailDialog
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;

    [Parameter] public ReleaseDto Release { get; set; } = default!;
}
