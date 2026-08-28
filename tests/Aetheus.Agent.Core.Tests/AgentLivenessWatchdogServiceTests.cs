// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public class AgentLivenessWatchdogServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CheckAndRestartIfStalled_HealthyLoops_DoesNotRestart()
    {
        var (service, health, restarter, _) = BuildService();
        health.MarkHeartbeatSuccess();
        health.MarkPollingSuccess();

        var restarted = service.CheckAndRestartIfStalled();

        Assert.False(restarted);
        Assert.Null(restarter.Reason);
    }

    [Fact]
    public void CheckAndRestartIfStalled_HeartbeatStopsProgressing_RestartsProcess()
    {
        var (service, health, restarter, time) = BuildService();
        health.MarkHeartbeatSuccess();
        health.MarkPollingSuccess();
        time.Advance(TimeSpan.FromSeconds(113));
        health.MarkPollingSuccess();

        var restarted = service.CheckAndRestartIfStalled();

        Assert.True(restarted);
        Assert.Contains("heartbeat loop", restarter.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckAndRestartIfStalled_PollingStopsProgressing_RestartsProcess()
    {
        var (service, health, restarter, time) = BuildService();
        health.MarkHeartbeatSuccess();
        health.MarkPollingSuccess();
        time.Advance(TimeSpan.FromSeconds(113));
        health.MarkHeartbeatSuccess();

        var restarted = service.CheckAndRestartIfStalled();

        Assert.True(restarted);
        Assert.Contains("polling loop", restarter.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckAndRestartIfStalled_NotEnrolled_DoesNotRestart()
    {
        var (service, health, restarter, time) = BuildService(isEnrolled: false);
        health.MarkHeartbeatSuccess();
        health.MarkPollingSuccess();
        time.Advance(TimeSpan.FromMinutes(10));

        var restarted = service.CheckAndRestartIfStalled();

        Assert.False(restarted);
        Assert.Null(restarter.Reason);
    }

    [Fact]
    public void CheckAndRestartIfStalled_ControlPlaneOutage_DoesNotRestartFleet()
    {
        var (service, health, restarter, time) = BuildService();
        health.MarkHeartbeatSuccess();
        health.MarkPollingSuccess();
        time.Advance(TimeSpan.FromMinutes(10));

        var restarted = service.CheckAndRestartIfStalled();

        Assert.False(restarted);
        Assert.Null(restarter.Reason);
    }

    [Fact]
    public void CheckAndRestartIfStalled_LocalLoopStallWithActiveTask_DefersRestart()
    {
        var (service, health, restarter, time) = BuildService();
        health.MarkHeartbeatSuccess();
        health.MarkPollingSuccess();
        health.BeginTask();
        time.Advance(TimeSpan.FromSeconds(113));
        health.MarkPollingSuccess();

        var restarted = service.CheckAndRestartIfStalled();

        Assert.False(restarted);
        Assert.Null(restarter.Reason);
        health.EndTask();
    }

    [Fact]
    public void CheckAndRestartIfStalled_LocalLoopStallWithTrackedProcess_DefersRestart()
    {
        var (service, health, restarter, time) = BuildService();
        health.MarkHeartbeatSuccess();
        health.MarkPollingSuccess();
        using var process = Process.GetCurrentProcess();
        using var activity = health.TrackProcess(process);
        time.Advance(TimeSpan.FromSeconds(113));
        health.MarkPollingSuccess();

        var restarted = service.CheckAndRestartIfStalled();

        Assert.False(restarted);
        Assert.Null(restarter.Reason);
        Assert.Equal(1, health.ActiveProcessCount);
    }

    [Fact]
    public void CheckAndRestartIfStalled_LocalLoopStall_WaitsForPerAgentJitter()
    {
        var (service, health, restarter, time) = BuildService();
        health.MarkHeartbeatSuccess();
        health.MarkPollingSuccess();
        time.Advance(TimeSpan.FromSeconds(111));
        health.MarkPollingSuccess();

        var restarted = service.CheckAndRestartIfStalled();

        Assert.False(restarted);
        Assert.Null(restarter.Reason);
    }

    private static (
        AgentLivenessWatchdogService Service,
        AgentRuntimeHealth Health,
        RecordingRestarter Restarter,
        FakeTimeProvider Time) BuildService(bool isEnrolled = true)
    {
        var time = new FakeTimeProvider(Start);
        var health = new AgentRuntimeHealth(time);
        var agentState = new AgentState { ServerId = 7 };
        var enrollment = Substitute.For<IEnrollmentService>();
        enrollment.IsEnrolled.Returns(isEnrolled);
        var restarter = new RecordingRestarter();
        var options = Options.Create(new AetheusAgentOptions
        {
            HeartbeatIntervalSeconds = 30,
            HeartbeatCollectionTimeoutSeconds = 15,
            PollingIntervalSeconds = 10
        });
        var service = new AgentLivenessWatchdogService(
            health,
            agentState,
            enrollment,
            restarter,
            time,
            options,
            NullLogger<AgentLivenessWatchdogService>.Instance);
        return (service, health, restarter, time);
    }

    private sealed class RecordingRestarter : IAgentProcessRestarter
    {
        public string? Reason { get; private set; }

        public void Restart(string reason) => Reason = reason;
    }
}
