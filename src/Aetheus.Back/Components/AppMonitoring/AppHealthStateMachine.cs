// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.AppMonitoring;

/// <summary>
/// Pure anti-flapping availability state machine (PLAN-001 phase 1). Extracted as a static so the
/// threshold / flapping / Unknown-initial behaviour is unit-testable without EF or a service.
/// </summary>
public static class AppHealthStateMachine
{
    public readonly record struct Transition(
        AppHealthStatus Status,
        int ConsecutiveFailures,
        int ConsecutiveSuccesses,
        bool Changed);

    /// <summary>
    /// Applies one probe result to the current state. <see cref="AppHealthStatus.Down"/> is declared only
    /// after <paramref name="failureThreshold"/> consecutive failures; <see cref="AppHealthStatus.Up"/> only
    /// after <paramref name="recoveryThreshold"/> consecutive successes. The app stays
    /// <see cref="AppHealthStatus.Unknown"/> until a threshold is first crossed - never green by default.
    /// </summary>
    public static Transition Apply(
        AppHealthStatus current,
        int consecutiveFailures,
        int consecutiveSuccesses,
        bool isUp,
        int failureThreshold,
        int recoveryThreshold)
    {
        var fails = failureThreshold < 1 ? 1 : failureThreshold;
        var recovers = recoveryThreshold < 1 ? 1 : recoveryThreshold;

        if (isUp)
        {
            consecutiveSuccesses++;
            consecutiveFailures = 0;
        }
        else
        {
            consecutiveFailures++;
            consecutiveSuccesses = 0;
        }

        var next = current;
        if (isUp && consecutiveSuccesses >= recovers)
            next = AppHealthStatus.Up;
        else if (!isUp && consecutiveFailures >= fails)
            next = AppHealthStatus.Down;

        return new Transition(next, consecutiveFailures, consecutiveSuccesses, next != current);
    }
}
