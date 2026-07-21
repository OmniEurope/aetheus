// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Aetheus.Front.Pages.Pipelines;

public partial class PipelineRunResultsTabs
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public PipelineRunDto Run { get; set; } = default!;
    [Parameter, EditorRequired] public PipelineRunMetrics Metrics { get; set; } = default!;
    [Parameter, EditorRequired] public IReadOnlyDictionary<int, List<TaskLogDto>> StepLogs { get; set; } = default!;
    [Parameter, EditorRequired] public IReadOnlyList<PipelineStepRunDto> LintSteps { get; set; } = default!;
    [Parameter] public bool HasLintTab { get; set; }
    [Parameter] public EventCallback<int> CoverageLogsRequested { get; set; }
    [Parameter] public EventCallback<int> LintLogsRequested { get; set; }

    private static string CoverageProgressClass(double rate) => rate switch
    {
        < 0.6 => "coverage-progress coverage-progress-danger",
        < 0.8 => "coverage-progress coverage-progress-warning",
        _ => "coverage-progress coverage-progress-success"
    };
}
