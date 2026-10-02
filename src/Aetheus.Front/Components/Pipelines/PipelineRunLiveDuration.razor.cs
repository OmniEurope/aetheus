// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

public partial class PipelineRunLiveDuration : LiveDurationComponentBase
{
    [Parameter] public DateTime? StartedAt { get; set; }
    [Parameter] public DateTime? CompletedAt { get; set; }
    [Parameter] public bool IsRunning { get; set; }

    private string Duration => PipelineRunFormatting.FormatDuration(StartedAt, CompletedAt);
    protected override bool IsLive => IsRunning && StartedAt is not null;

    protected override IDisposable CreateTimer(TimerCallback callback) => new System.Threading.Timer(
        callback,
        null,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(1));
}
