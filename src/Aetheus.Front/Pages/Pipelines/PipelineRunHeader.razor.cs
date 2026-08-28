// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

public partial class PipelineRunHeader
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public PipelineRunDto Run { get; set; } = default!;
    [Parameter, EditorRequired] public string PipelineHref { get; set; } = string.Empty;
    [Parameter] public string? GitBranch { get; set; }
    [Parameter] public int BlockingWarningCount { get; set; }
    [Parameter] public int InfoWarningCount { get; set; }
    [Parameter] public bool RunInProgress { get; set; }
    [Parameter] public bool CanRerun { get; set; }
    [Parameter] public bool HasFailedSteps { get; set; }
    [Parameter] public bool Cancelling { get; set; }
    [Parameter] public bool Rerunning { get; set; }
    [Parameter] public bool Retrying { get; set; }
    [Parameter] public EventCallback VariablesRequested { get; set; }
    [Parameter] public EventCallback EditRequested { get; set; }
    [Parameter] public EventCallback CancelRequested { get; set; }
    [Parameter] public EventCallback<RadzenSplitButtonItem?> RerunRequested { get; set; }
    [Parameter] public EventCallback RetryFailedRequested { get; set; }
}
