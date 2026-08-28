// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

public partial class PipelineRunGateReport
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Parameter, EditorRequired] public AnalysisRunGateDto Gate { get; set; } = default!;

    private IEnumerable<IGrouping<AnalysisCategory, AnalysisRunGateFindingDto>> Groups => Gate.Findings
        .GroupBy(finding => finding.Category)
        .OrderByDescending(group => group.Max(finding => finding.Severity))
        .ThenBy(group => group.Key);

    private void OpenFinding(int findingId) => Navigation.NavigateTo($"/analysis/findings/{findingId}");
}
