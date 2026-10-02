// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Projects.ProjectDetailSections;

public partial class ReleaseDetailDialog
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private OmniDialogService Dialog { get; set; } = default!;

    [Parameter] public ReleaseDto Release { get; set; } = default!;

    private string BuildNumberDisplay => Release.BuildNumber.ToString();
    private string DetectedAtDisplay => Release.DetectedAt == default ? "-" : Release.DetectedAt.ToString("g");
    private string PublishedAtDisplay => Release.PublishedAt?.ToString("g") ?? string.Empty;
}
