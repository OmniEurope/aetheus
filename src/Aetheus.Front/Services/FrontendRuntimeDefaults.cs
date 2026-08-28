// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Services;

/// <summary>Operational HTTP and realtime retry defaults shared by the browser services.</summary>
internal static class FrontendRuntimeDefaults
{
    public static readonly TimeSpan ApiAttemptTimeout = TimeSpan.FromSeconds(35);
    public static readonly TimeSpan ApiTotalRequestTimeout = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan ApiCircuitSamplingDuration = TimeSpan.FromSeconds(80);
    public static readonly TimeSpan SignalRStartTimeout = TimeSpan.FromSeconds(10);
    public const int ApiMaximumRetryAttempts = 1;

    public static IReadOnlyList<TimeSpan> SignalRRetryDelays { get; } =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];
}
