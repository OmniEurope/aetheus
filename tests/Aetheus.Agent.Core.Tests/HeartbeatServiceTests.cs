// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Collectors;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.DTOs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public class HeartbeatServiceTests
{
    private readonly IServerApiClient _apiClientMock = Substitute.For<IServerApiClient>();
    private readonly IMetricsCollector _metricsCollectorMock = Substitute.For<IMetricsCollector>();
    private readonly IServiceCollector _serviceCollectorMock = Substitute.For<IServiceCollector>();
    private readonly IDockerCollector _dockerCollectorMock = Substitute.For<IDockerCollector>();
    private readonly IApacheCollector _apacheCollectorMock = Substitute.For<IApacheCollector>();
    private readonly ICertbotCollector _certbotCollectorMock = Substitute.For<ICertbotCollector>();
    private readonly IMailCollector _mailCollectorMock = Substitute.For<IMailCollector>();
    private readonly ITeamspeakCollector _teamspeakCollectorMock = Substitute.For<ITeamspeakCollector>();
    private readonly IPortsentryCollector _portsentryCollectorMock = Substitute.For<IPortsentryCollector>();
    private readonly IRkhunterCollector _rkhunterCollectorMock = Substitute.For<IRkhunterCollector>();
    private readonly ISecurityUpdatesCollector _securityUpdatesCollectorMock = Substitute.For<ISecurityUpdatesCollector>();
    private readonly IFirewallCollector _firewallCollectorMock = Substitute.For<IFirewallCollector>();
    private readonly ISudoersHashCollector _sudoersHashCollectorMock = Substitute.For<ISudoersHashCollector>();
    private readonly IDockerStorageMaintenance _dockerStorageMaintenanceMock = Substitute.For<IDockerStorageMaintenance>();
    private readonly AgentState _agentState;
    private readonly EnrollmentService _enrollment;
    private readonly IOptions<AetheusAgentOptions> _options;

    public HeartbeatServiceTests()
    {
        _agentState = new AgentState
        {
            ServerId = 1,
            BearerToken = "test-token"
        };

        _options = Options.Create(new AetheusAgentOptions
        {
            ServerUrl = "http://localhost:5301",
            HeartbeatIntervalSeconds = 1,
            HeartbeatCollectionTimeoutSeconds = 1
        });

        _enrollment = CreateEnrolledEnrollmentService();

        _metricsCollectorMock.Collect().Returns(new ServerHeartbeatDto
        {
            CpuPercent = 25.0,
            MemoryUsedMb = 2048,
            MemoryTotalMb = 8192,
            Disks = [],
            Services = []
        });

        _serviceCollectorMock.CollectAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ServiceInfoDto>());

        _dockerCollectorMock.CollectAllAsync(Arg.Any<CancellationToken>())
            .Returns(new DockerDataDto());

        _apacheCollectorMock.CollectAsync(Arg.Any<CancellationToken>())
            .Returns(new ApacheDataDto());

        _certbotCollectorMock.CollectAsync(Arg.Any<CancellationToken>())
            .Returns(new CertbotDataDto());

        _mailCollectorMock.CollectAsync(Arg.Any<CancellationToken>())
            .Returns(new MailDataDto());

        _teamspeakCollectorMock.CollectAsync(Arg.Any<CancellationToken>())
            .Returns(new TeamspeakDataDto());

        _portsentryCollectorMock.CollectAsync(Arg.Any<CancellationToken>())
            .Returns(new PortsentryDataDto());

        _rkhunterCollectorMock.CollectAsync(Arg.Any<CancellationToken>())
            .Returns(new RkhunterDataDto());

        _securityUpdatesCollectorMock.CollectAsync(Arg.Any<CancellationToken>())
            .Returns(new SecurityUpdatesDataDto());

        _firewallCollectorMock.CollectAsync(Arg.Any<CancellationToken>())
            .Returns(new FirewallDataDto());

        _sudoersHashCollectorMock.CollectAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string>());

        _dockerStorageMaintenanceMock.CollectDiagnosticsAsync(Arg.Any<CancellationToken>())
            .Returns(new StorageDiagnosticsDto { BuildCacheBytes = 1234 });
    }

    [Fact]
    public async Task CollectAndSendHeartbeatAsync_SendsHeartbeatWhenEnrolled()
    {
        var service = CreateService();

        // E-3: drive one iteration directly - deterministic, no PeriodicTimer / sleep.
        await service.CollectAndSendHeartbeatAsync(TestContext.Current.CancellationToken);

        await _apiClientMock.Received(1).SendHeartbeatAsync(1, Arg.Any<ServerHeartbeatDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CollectAndSendHeartbeatAsync_StampsAgentVersionAndCollectedMetrics()
    {
        var service = CreateService();

        await service.CollectAndSendHeartbeatAsync(TestContext.Current.CancellationToken);

        // The iteration must merge the collected metrics and stamp the agent version
        // before posting - guards the heartbeat payload, not just that a call happened.
        await _apiClientMock.Received(1).SendHeartbeatAsync(1, Arg.Is<ServerHeartbeatDto>(h =>
                h.CpuPercent == 25.0
                && h.MemoryUsedMb == 2048
                && h.MemoryTotalMb == 8192
                && h.StorageDiagnostics.BuildCacheBytes == 1234
                && h.SudoersInventoryAvailable == true
                && !string.IsNullOrEmpty(h.AgentVersion)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CollectAndSendHeartbeatAsync_ApiFailure_DoesNotCorruptNextIteration()
    {
        _apiClientMock.SendHeartbeatAsync(1, Arg.Any<ServerHeartbeatDto>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new HttpRequestException("Connection refused"),
                _ => (ServerHeartbeatResponseDto?)null);

        var service = CreateService();

        // First iteration throws (the loop swallows it and retries next tick); the SECOND
        // iteration must still post a heartbeat. Asserting two real iterations replaces the
        // old "sleep 4s and hope ≥2 calls landed".
        await Assert.ThrowsAsync<HttpRequestException>(() => service.CollectAndSendHeartbeatAsync(TestContext.Current.CancellationToken));
        await service.CollectAndSendHeartbeatAsync(TestContext.Current.CancellationToken);

        await _apiClientMock.Received(2).SendHeartbeatAsync(1, Arg.Any<ServerHeartbeatDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ApiFailure_RetriesOnNextTimeProviderTick()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero));
        var firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        _apiClientMock.SendHeartbeatAsync(1, Arg.Any<ServerHeartbeatDto>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                if (attempt == 1)
                {
                    firstAttempt.TrySetResult();
                    throw new HttpRequestException("Connection refused");
                }

                secondAttempt.TrySetResult();
                return (ServerHeartbeatResponseDto?)null;
            });
        var service = CreateService(time);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.LoopStarted.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));
        await firstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));
        await secondAttempt.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, Volatile.Read(ref attempts));
    }

    [Fact]
    public async Task ExecuteAsync_NotEnrolled_DoesNotSendHeartbeat()
    {
        var notEnrolledState = new AgentState();
        var notEnrolled = CreateNotEnrolledEnrollmentService(notEnrolledState);

        var service = new HeartbeatService(
            _apiClientMock,
            notEnrolled,
            _metricsCollectorMock,
            _serviceCollectorMock,
            _dockerCollectorMock,
            _apacheCollectorMock,
            _certbotCollectorMock,
            _mailCollectorMock,
            _teamspeakCollectorMock,
            _portsentryCollectorMock,
            _rkhunterCollectorMock,
            _securityUpdatesCollectorMock,
            _firewallCollectorMock,
            _sudoersHashCollectorMock,
            _dockerStorageMaintenanceMock,
            Substitute.For<IShellRunner>(),
            notEnrolledState,
            new AgentRuntimeHealth(TimeProvider.System),
            TimeProvider.System,
            _options,
            NullLogger<HeartbeatService>.Instance);

        // While not enrolled the loop parks on its wait-gate and never reaches a send.
        // Start then immediately stop: the gate is never crossed - deterministic, no sleep.
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await cts.CancelAsync();
        await service.StopAsync(TestContext.Current.CancellationToken);

        await _apiClientMock.DidNotReceive().SendHeartbeatAsync(Arg.Any<int>(), Arg.Any<ServerHeartbeatDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CollectAndSendHeartbeatAsync_CollectorFailure_RecoversOnNextIteration()
    {
        _metricsCollectorMock.Collect()
            .Returns(
                _ => throw new InvalidOperationException("Collector error"),
                _ => new ServerHeartbeatDto { CpuPercent = 50, MemoryUsedMb = 2048, MemoryTotalMb = 8192 });

        var service = CreateService();

        // A collector throwing must not poison the loop: the next iteration collects and sends.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CollectAndSendHeartbeatAsync(TestContext.Current.CancellationToken));
        await service.CollectAndSendHeartbeatAsync(TestContext.Current.CancellationToken);

        await _apiClientMock.Received(1).SendHeartbeatAsync(1, Arg.Any<ServerHeartbeatDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CollectAndSendHeartbeatAsync_HungInventory_SendsDegradedHeartbeatWithinBudget()
    {
        var hungCollector = new TaskCompletionSource<DockerDataDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dockerCollectorMock.CollectAllAsync(Arg.Any<CancellationToken>()).Returns(hungCollector.Task);
        var service = CreateService();

        await service.CollectAndSendHeartbeatAsync(TestContext.Current.CancellationToken);

        await _apiClientMock.Received(1).SendHeartbeatAsync(1, Arg.Is<ServerHeartbeatDto>(heartbeat =>
                heartbeat.CapabilityDiagnostics.Any(diagnostic => diagnostic.Contains("timed out", StringComparison.OrdinalIgnoreCase))),
            Arg.Any<CancellationToken>());
        hungCollector.TrySetCanceled(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CollectAndSendHeartbeatAsync_PermanentlyHungInventory_IsQuarantinedSingleFlight()
    {
        var hungCollector = new TaskCompletionSource<DockerDataDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dockerCollectorMock.CollectAllAsync(Arg.Any<CancellationToken>()).Returns(hungCollector.Task);
        var service = CreateService();

        await service.CollectAndSendHeartbeatAsync(TestContext.Current.CancellationToken);
        await service.CollectAndSendHeartbeatAsync(TestContext.Current.CancellationToken);

        await _dockerCollectorMock.Received(1).CollectAllAsync(Arg.Any<CancellationToken>());
        await _apiClientMock.Received(2).SendHeartbeatAsync(1, Arg.Is<ServerHeartbeatDto>(heartbeat =>
                heartbeat.CapabilityDiagnostics.Any(diagnostic =>
                    diagnostic.Contains("quarantined", StringComparison.OrdinalIgnoreCase))),
            Arg.Any<CancellationToken>());
        hungCollector.TrySetCanceled(TestContext.Current.CancellationToken);
    }

    private HeartbeatService CreateService(TimeProvider? timeProvider = null)
    {
        var time = timeProvider ?? TimeProvider.System;
        return new(
        _apiClientMock,
        _enrollment,
        _metricsCollectorMock,
        _serviceCollectorMock,
        _dockerCollectorMock,
        _apacheCollectorMock,
        _certbotCollectorMock,
        _mailCollectorMock,
        _teamspeakCollectorMock,
        _portsentryCollectorMock,
        _rkhunterCollectorMock,
        _securityUpdatesCollectorMock,
        _firewallCollectorMock,
        _sudoersHashCollectorMock,
        _dockerStorageMaintenanceMock,
        Substitute.For<IShellRunner>(),
        _agentState,
        new AgentRuntimeHealth(time),
        time,
        _options,
        NullLogger<HeartbeatService>.Instance);
    }

    private EnrollmentService CreateEnrolledEnrollmentService()
    {
        var mockConfig = Substitute.For<Microsoft.Extensions.Configuration.IConfiguration>();
        return new EnrollmentService(
            _apiClientMock,
            _options,
            _agentState,
            mockConfig,
            Substitute.For<ICredentialProtector>(),
            Substitute.For<IShellRunner>(),
            TimeProvider.System,
            NullLogger<EnrollmentService>.Instance);
    }

    private EnrollmentService CreateNotEnrolledEnrollmentService(AgentState state) =>
        new(
            _apiClientMock,
            _options,
            state,
            Substitute.For<Microsoft.Extensions.Configuration.IConfiguration>(),
            Substitute.For<ICredentialProtector>(),
            Substitute.For<IShellRunner>(),
            TimeProvider.System,
            NullLogger<EnrollmentService>.Instance);
}
