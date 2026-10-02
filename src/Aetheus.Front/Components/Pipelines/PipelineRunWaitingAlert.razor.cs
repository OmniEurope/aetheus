// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

/// <summary>
/// Why the run on screen is not advancing. Its own component so the run page stays within the
/// file-size budget (FileSizeAuditTests); the aggregation across triggered children lives in
/// <see cref="PipelineRunWaitingSummary"/>.
/// </summary>
public partial class PipelineRunWaitingAlert
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public IReadOnlyList<PipelineRunWaitingSummary.WaitView> Waits { get; set; } = [];
}
