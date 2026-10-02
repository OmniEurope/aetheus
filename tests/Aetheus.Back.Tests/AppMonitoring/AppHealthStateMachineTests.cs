// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring;

namespace Aetheus.Back.Tests.AppMonitoring;

public class AppHealthStateMachineTests
{
    [Fact]
    public void Unknown_StaysUnknown_UntilRecoveryThresholdMet()
    {
        // recovery=2: a single Up must NOT flip an unseen app to green (no-fake rule).
        var t1 = AppHealthStateMachine.Apply(AppHealthStatus.Unknown, 0, 0, isUp: true, failureThreshold: 3, recoveryThreshold: 2);
        Assert.Equal(AppHealthStatus.Unknown, t1.Status);
        Assert.False(t1.Changed);
        Assert.Equal(1, t1.ConsecutiveSuccesses);

        var t2 = AppHealthStateMachine.Apply(t1.Status, t1.ConsecutiveFailures, t1.ConsecutiveSuccesses, isUp: true, 3, 2);
        Assert.Equal(AppHealthStatus.Up, t2.Status);
        Assert.True(t2.Changed);
    }

    [Fact]
    public void Down_DeclaredOnlyAfterFailureThresholdConsecutiveFailures()
    {
        var s = AppHealthStatus.Up;
        int fails = 0, succ = 5;

        var a = AppHealthStateMachine.Apply(s, fails, succ, isUp: false, 3, 2);
        Assert.Equal(AppHealthStatus.Degraded, a.Status); // early warning without declaring Down
        Assert.True(a.Changed);

        var b = AppHealthStateMachine.Apply(a.Status, a.ConsecutiveFailures, a.ConsecutiveSuccesses, isUp: false, 3, 2);
        Assert.Equal(AppHealthStatus.Degraded, b.Status); // 2 failures, still not Down

        var c = AppHealthStateMachine.Apply(b.Status, b.ConsecutiveFailures, b.ConsecutiveSuccesses, isUp: false, 3, 2);
        Assert.Equal(AppHealthStatus.Down, c.Status); // 3 failures -> Down
        Assert.True(c.Changed);
    }

    [Fact]
    public void Flapping_SingleFailureBetweenSuccesses_DoesNotTripDown()
    {
        // Up, one failure, then success: surfaces Degraded but never reaches Down.
        var a = AppHealthStateMachine.Apply(AppHealthStatus.Up, 0, 4, isUp: false, 3, 2);
        Assert.Equal(AppHealthStatus.Degraded, a.Status);
        var b = AppHealthStateMachine.Apply(a.Status, a.ConsecutiveFailures, a.ConsecutiveSuccesses, isUp: true, 3, 2);
        Assert.Equal(AppHealthStatus.Degraded, b.Status);
        Assert.Equal(0, b.ConsecutiveFailures); // failure streak reset by the success
    }

    [Fact]
    public void Recovery_FromDown_NeedsRecoveryThresholdConsecutiveSuccesses()
    {
        var s = AppHealthStatus.Down;
        var a = AppHealthStateMachine.Apply(s, 5, 0, isUp: true, 3, 2);
        Assert.Equal(AppHealthStatus.Degraded, a.Status); // recovery started, not green yet
        Assert.True(a.Changed);

        var b = AppHealthStateMachine.Apply(a.Status, a.ConsecutiveFailures, a.ConsecutiveSuccesses, isUp: true, 3, 2);
        Assert.Equal(AppHealthStatus.Up, b.Status); // 2 successes -> Up
        Assert.True(b.Changed);
    }

    [Fact]
    public void Thresholds_ClampedToAtLeastOne()
    {
        // A misconfigured 0 threshold must not make the app un-flippable; it is treated as 1.
        var t = AppHealthStateMachine.Apply(AppHealthStatus.Unknown, 0, 0, isUp: false, failureThreshold: 0, recoveryThreshold: 0);
        Assert.Equal(AppHealthStatus.Down, t.Status);
        Assert.True(t.Changed);
    }
}
