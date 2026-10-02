// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Pipelines;

namespace Aetheus.Front.Tests.Pipelines;

/// <summary>
/// A candidate run is an orchestration: its own steps are triggers that sit in Running while the
/// child does the work. So the wait a user needs to read is almost never the parent's own.
/// </summary>
public sealed class RunWaitingSummaryTests
{
    private static PipelineRunDto Run(
        int id, string name, string? waiting = null, DateTime? since = null) => new()
        {
            Id = id,
            PipelineName = name,
            Status = PipelineStatus.Running,
            WaitingReason = waiting,
            WaitingSince = since
        };

    [Fact]
    public void ARunWaitingOnItsOwn_ReportsThatWithNoSource()
    {
        var since = new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc);
        var views = PipelineRunWaitingSummary.Build(
            Run(1, "aetheus-ci", "Stage 'Build images' waits for a configured pipeline runner.", since), null);

        var view = Assert.Single(views);
        Assert.Null(view.Source);
        Assert.Contains("Build images", view.Reason);
        Assert.Equal(since, view.Since);
    }

    [Fact]
    public void AChildWaiting_IsAttributedToItsPipeline()
    {
        var parent = Run(1, "aetheus-candidate");
        var child = Run(2, "aetheus-ci", "Stage 'Build images' waits for a configured pipeline runner.");

        var views = PipelineRunWaitingSummary.Build(parent, new Dictionary<int, PipelineRunDto> { [2] = child });

        var view = Assert.Single(views);
        Assert.Equal("aetheus-ci", view.Source);
    }

    [Fact]
    public void TheRunsOwnWaitComesFirst_ThenChildrenInIdOrder()
    {
        var parent = Run(1, "aetheus-candidate", "parent waits");
        var children = new Dictionary<int, PipelineRunDto>
        {
            [9] = Run(9, "aetheus-qa", "qa waits"),
            [4] = Run(4, "aetheus-ci", "ci waits")
        };

        var views = PipelineRunWaitingSummary.Build(parent, children);

        Assert.Equal([null, "aetheus-ci", "aetheus-qa"], views.Select(view => view.Source));
    }

    [Fact]
    public void ARunProgressing_ReportsNothing()
    {
        var parent = Run(1, "aetheus-candidate");
        var children = new Dictionary<int, PipelineRunDto> { [2] = Run(2, "aetheus-ci") };

        Assert.Empty(PipelineRunWaitingSummary.Build(parent, children));
    }

    [Fact]
    public void TheRunItselfInTheChildCache_IsNotCountedTwice()
    {
        // The page's cache is seeded from the run tree and can contain the run it was built from.
        var run = Run(1, "aetheus-candidate", "parent waits");

        var views = PipelineRunWaitingSummary.Build(run, new Dictionary<int, PipelineRunDto> { [1] = run });

        Assert.Single(views);
    }
}
