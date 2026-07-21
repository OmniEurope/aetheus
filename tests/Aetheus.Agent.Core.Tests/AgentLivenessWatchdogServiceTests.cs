// SPDX-License-Identifier: EUPL-1.2
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
        health.MarkHeartbeatProgress();
        health.MarkPollingProgress();

        var restarted = service.CheckAndRestartIfStalled();

        Assert.False(restarted);
        Assert.Null(restarter.Reason);
    }

    [Fact]
    public void CheckAndRestartIfStalled_HeartbeatStopsProgressing_RestartsProcess()
    {
        var (service, health, restarter, time) = BuildService();
        health.MarkHeartbeatProgress();
        health.MarkPollingProgress();
        time.Advance(TimeSpan.FromSeconds(91));
        health.MarkPollingProgress();

        var restarted = service.CheckAndRestartIfStalled();

        Assert.True(restarted);
        Assert.Contains("heartbeat loop", restarter.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckAndRestartIfStalled_PollingStopsProgressing_RestartsProcess()
    {
        var (service, health, restarter, time) = BuildService();
        health.MarkHeartbeatProgress();
        health.MarkPollingProgress();
        time.Advance(TimeSpan.FromSeconds(91));
        health.MarkHeartbeatProgress();

        var restarted = service.CheckAndRestartIfStalled();

        Assert.True(restarted);
        Assert.Contains("polling loop", restarter.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckAndRestartIfStalled_NotEnrolled_DoesNotRestart()
    {
        var (service, health, restarter, time) = BuildService(isEnrolled: false);
        health.MarkHeartbeatProgress();
        health.MarkPollingProgress();
        time.Advance(TimeSpan.FromMinutes(10));

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
