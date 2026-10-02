// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

/// <summary>
/// The preflight record kept on a run. Its own component so the run page stays within the file-size
/// budget (FileSizeAuditTests) and so the card can be shown wherever a run is inspected.
/// </summary>
public partial class PipelineRunPreflightRecord
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public IReadOnlyList<PreflightCheckDto> Checks { get; set; } = [];
}
