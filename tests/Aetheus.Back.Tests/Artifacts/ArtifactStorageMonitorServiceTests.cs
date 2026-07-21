// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Artifacts;
using Aetheus.Back.Hubs;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Back.Tests.Artifacts;

public sealed class ArtifactStorageMonitorServiceTests
{
    [Fact]
    public async Task EvaluateAsync_WhenPhysicalBudgetExceeded_SendsCriticalAlert()
    {
        var root = Directory.CreateTempSubdirectory("aetheus-artifact-monitor-");
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root.FullName, "artifact.bin"), new byte[32], cancellationToken: TestContext.Current.CancellationToken);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ArtifactStorage:BasePath"] = root.FullName,
                    ["ArtifactStorage:VolumeBudgetBytes"] = "16",
                    ["ArtifactStorage:GrowthWarningBytesPerDay"] = "0"
                })
                .Build();
            var proxy = Substitute.For<IClientProxy>();
            var clients = Substitute.For<IHubClients>();
            clients.Group(Arg.Any<string>()).Returns(proxy);
            var hub = Substitute.For<IHubContext<AlertHub>>();
            hub.Clients.Returns(clients);
            var service = new ArtifactStorageMonitorService(
                configuration, hub, TimeProvider.System, NullLogger<ArtifactStorageMonitorService>.Instance);
            AlertTriggeredDto? alert = null;
            proxy.SendCoreAsync("AlertTriggered", Arg.Do<object?[]>(arguments =>
                    alert = Assert.IsType<AlertTriggeredDto>(arguments[0])), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            await service.EvaluateAsync(TestContext.Current.CancellationToken);

            await proxy.Received(1).SendCoreAsync(
                "AlertTriggered", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
            Assert.NotNull(alert);
            Assert.Equal("ArtifactStorage", alert.RuleName);
            Assert.Equal("ArtifactVolumeBytes", alert.Metric);
            Assert.Equal("Critical", alert.Severity);
            Assert.Equal(16, alert.Threshold);
            Assert.Equal(32, ArtifactStorageMonitorService.MeasureDirectoryBytes(root.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task EvaluateAsync_NormalizesGrowthAndAppliesSixHourCooldown()
    {
        var root = Directory.CreateTempSubdirectory("aetheus-artifact-growth-");
        try
        {
            var artifact = Path.Combine(root.FullName, "artifact.bin");
            await File.WriteAllBytesAsync(artifact, [], cancellationToken: TestContext.Current.CancellationToken);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ArtifactStorage:BasePath"] = root.FullName,
                    ["ArtifactStorage:VolumeBudgetBytes"] = "10000",
                    ["ArtifactStorage:GrowthWarningBytesPerDay"] = "100"
                }).Build();
            var proxy = Substitute.For<IClientProxy>();
            var clients = Substitute.For<IHubClients>();
            clients.Group(Arg.Any<string>()).Returns(proxy);
            var hub = Substitute.For<IHubContext<AlertHub>>();
            hub.Clients.Returns(clients);
            var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.Zero));
            var service = new ArtifactStorageMonitorService(
                configuration, hub, time, NullLogger<ArtifactStorageMonitorService>.Instance);

            await service.EvaluateAsync(TestContext.Current.CancellationToken);
            await proxy.DidNotReceive().SendCoreAsync(
                "AlertTriggered", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());

            await File.WriteAllBytesAsync(artifact, new byte[10], cancellationToken: TestContext.Current.CancellationToken);
            time.Advance(TimeSpan.FromHours(1));
            await service.EvaluateAsync(TestContext.Current.CancellationToken);
            await proxy.Received(1).SendCoreAsync(
                "AlertTriggered",
                Arg.Is<object?[]>(arguments =>
                    ((AlertTriggeredDto)arguments[0]!).Severity == "Warning"
                    && ((AlertTriggeredDto)arguments[0]!).Threshold == 100),
                Arg.Any<CancellationToken>());

            await File.WriteAllBytesAsync(artifact, new byte[20], cancellationToken: TestContext.Current.CancellationToken);
            time.Advance(TimeSpan.FromHours(1));
            await service.EvaluateAsync(TestContext.Current.CancellationToken);
            await proxy.Received(1).SendCoreAsync(
                "AlertTriggered", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
