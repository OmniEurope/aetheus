// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.DTOs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Tests;

public sealed class DeploymentBuildRefusalOutboxTests : IDisposable
{
    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "aetheus-refusal-outbox-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StoredIncident_SurvivesOutboxRecreationUntilAcknowledged()
    {
        var report = new DeploymentBuildRefusalReport
        {
            IncidentId = Guid.NewGuid(),
            TaskId = 19,
            OccurredAtUtc = DateTime.UtcNow,
            Reason = "deployment-only refusal"
        };
        var first = CreateOutbox();
        await first.StoreAsync(report, TestContext.Current.CancellationToken);

        var recreated = CreateOutbox();
        var restored = await recreated.ReadAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(report, Assert.Single(restored));
        recreated.Remove(report.IncidentId);
        Assert.Empty(await recreated.ReadAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CorruptIncident_DoesNotBlockValidIncidents()
    {
        var report = new DeploymentBuildRefusalReport
        {
            IncidentId = Guid.NewGuid(),
            TaskId = 20,
            OccurredAtUtc = DateTime.UtcNow,
            Reason = "deployment-only refusal"
        };
        var outbox = CreateOutbox();
        await outbox.StoreAsync(report, TestContext.Current.CancellationToken);
        var directory = Path.Combine(_workDirectory, "outbox", "deployment-build-refusals");
        await File.WriteAllTextAsync(
            Path.Combine(directory, "corrupt.json"),
            "{",
            TestContext.Current.CancellationToken);

        var restored = await CreateOutbox().ReadAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(report, Assert.Single(restored));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDirectory))
            Directory.Delete(_workDirectory, recursive: true);
    }

    private DeploymentBuildRefusalOutbox CreateOutbox() =>
        new(
            Options.Create(new AetheusAgentOptions { WorkDirectory = _workDirectory }),
            NullLogger<DeploymentBuildRefusalOutbox>.Instance);
}
