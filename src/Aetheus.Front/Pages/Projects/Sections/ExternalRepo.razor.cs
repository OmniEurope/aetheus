// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Projects.Sections;

public partial class ExternalRepo
{
    [Parameter] public int Id { get; set; }
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    protected override void OnParametersSet() =>
        Nav.NavigateTo($"/git-repositories?projectId={Id}#external-repository", replace: true);
}
