// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

public partial class PipelineRunLogLines
{
    [Parameter, EditorRequired] public IReadOnlyList<TaskLogDto> Logs { get; set; } = default!;
    [Parameter] public bool Compact { get; set; }
    [Parameter] public bool HighlightLintWarnings { get; set; }
}
