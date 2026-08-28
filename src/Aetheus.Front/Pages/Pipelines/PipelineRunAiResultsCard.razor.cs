// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Pages.Pipelines;

/// <summary>
/// The AI-results card of a run's Overview tab, extracted from <c>PipelineRun.razor</c> during the
/// 2026-08-20 audit remediation (A360-75). It owns no state: the page keeps the results and handles
/// the click, so extracting it changed the file it lives in and nothing else.
/// </summary>
public partial class PipelineRunAiResultsCard
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired]
    public IReadOnlyList<AiRunResultDto> Results { get; set; } = [];

    /// <summary>Raised when the user opens one result; the page owns the dialog.</summary>
    [Parameter]
    public EventCallback<AiRunResultDto> OnView { get; set; }
}
