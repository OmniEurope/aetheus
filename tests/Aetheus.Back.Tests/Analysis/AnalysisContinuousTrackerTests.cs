// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Analysis;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using NSubstitute;

namespace Aetheus.Back.Tests.Analysis;

public sealed class AnalysisContinuousTrackerTests
{
    private static readonly DateTime Now = new(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ApplySuccessfulSnapshot_DoesNotDuplicateIdenticalObservation()
    {
        var tracking = new AnalysisTrackingProject { OrganizationId = 7, ProjectId = 9 };
        var vulnerabilities = new[]
        {
            new DependencyTrackVulnerability("CVE-1", "component", "1.0", "pkg:nuget/component@1.0",
                AnalysisSeverity.High, "NOT_SET")
        };

        AnalysisContinuousTracker.ApplySuccessfulSnapshot(tracking, vulnerabilities, 1, false, Now, out var first);
        AnalysisContinuousTracker.ApplySuccessfulSnapshot(tracking, vulnerabilities, 1, true, Now.AddMinutes(1), out var second);

        Assert.Single(first);
        Assert.Empty(second);
        Assert.Equal(7, first[0].OrganizationId);
        Assert.Equal(9, first[0].ProjectId);
    }

    [Fact]
    public async Task ProcessSbomAsync_LeavesFailurePersistenceToTheOutboxWorker()
    {
        var repository = Substitute.For<IAnalysisRepository>();
        repository.GetTrackingProjectAsync(9, Arg.Any<CancellationToken>()).Returns((AnalysisTrackingProject?)null);
        var client = Substitute.For<IDependencyTrackClient>();
        client.SubmitAndReadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<DependencyTrackSubmission>>(_ => throw new HttpRequestException("provider unavailable"));
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(Now));
        var tracker = new AnalysisContinuousTracker(repository, client, time);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            tracker.ProcessSbomAsync(
                new AnalysisReportDtoContext(1, 7, 9, "org-7/project", "commit"),
                "{}",
                TestContext.Current.CancellationToken));

        Assert.Contains("provider unavailable", exception.Message, StringComparison.Ordinal);
        await repository.DidNotReceive().SaveTrackingSnapshotAsync(
            Arg.Any<AnalysisTrackingProject>(),
            Arg.Any<IReadOnlyCollection<AnalysisVulnerabilityObservation>>(),
            Arg.Any<CancellationToken>());
    }
}
