// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests.Shared;

/// <summary>Recette R-119 and R-122: the catalogue order and the summary strip rules.</summary>
public class PipelineCatalogViewTests
{
    private static readonly Dictionary<int, PipelineFleetItemDto> NoFleet = [];

    private static PipelineDependencyDto Pipeline(int id, params PipelineStatus[] newestFirst) => new()
    {
        Id = id,
        Name = $"p{id}",
        RecentRuns = newestFirst.Select((status, index) => new PipelineRunSummaryDto { Id = id * 10 - index, Status = status }).ToList()
    };

    [Theory]
    [InlineData(PipelineStatus.Failed, PipelineCatalogStatus.Failed)]
    [InlineData(PipelineStatus.Pending, PipelineCatalogStatus.Running)]
    [InlineData(PipelineStatus.Running, PipelineCatalogStatus.Running)]
    [InlineData(PipelineStatus.WaitingForApproval, PipelineCatalogStatus.Running)]
    public void NewestRun_DecidesTheRunStatus(PipelineStatus newest, PipelineCatalogStatus expected)
    {
        var pipeline = Pipeline(1, newest, PipelineStatus.Success);

        Assert.True(PipelineCatalogView.Matches(pipeline, expected, NoFleet));
        Assert.False(PipelineCatalogView.Matches(pipeline, PipelineCatalogStatus.NeverRun, NoFleet));
    }

    [Theory]
    [InlineData(PipelineStatus.Success)]
    [InlineData(PipelineStatus.Cancelled)]
    [InlineData(PipelineStatus.Partial)]
    public void AnOlderFailure_DoesNotCountAsFailed(PipelineStatus newest)
    {
        var pipeline = Pipeline(1, newest, PipelineStatus.Failed);

        Assert.False(PipelineCatalogView.Matches(pipeline, PipelineCatalogStatus.Failed, NoFleet));
        Assert.False(PipelineCatalogView.Matches(pipeline, PipelineCatalogStatus.Running, NoFleet));
    }

    [Fact]
    public void APipelineWithoutRuns_IsNeverRun()
    {
        var pipeline = Pipeline(1);

        Assert.True(PipelineCatalogView.Matches(pipeline, PipelineCatalogStatus.NeverRun, NoFleet));
        Assert.False(PipelineCatalogView.Matches(pipeline, PipelineCatalogStatus.Failed, NoFleet));
    }

    [Fact]
    public void Outdated_ComesFromTheFleetFreshness()
    {
        var fleet = new Dictionary<int, PipelineFleetItemDto>
        {
            [1] = new() { PipelineId = 1, Freshness = PipelineFleetFreshness.Outdated },
            [2] = new() { PipelineId = 2, Freshness = PipelineFleetFreshness.Current }
        };

        Assert.True(PipelineCatalogView.Matches(Pipeline(1), PipelineCatalogStatus.Outdated, fleet));
        Assert.False(PipelineCatalogView.Matches(Pipeline(2), PipelineCatalogStatus.Outdated, fleet));
        Assert.False(PipelineCatalogView.Matches(Pipeline(3), PipelineCatalogStatus.Outdated, fleet));
    }

    [Fact]
    public void Count_CountsEveryMatchingRow()
    {
        var pipelines = new[]
        {
            Pipeline(1, PipelineStatus.Failed),
            Pipeline(2, PipelineStatus.Failed, PipelineStatus.Success),
            Pipeline(3, PipelineStatus.Success),
            Pipeline(4)
        };

        Assert.Equal(2, PipelineCatalogView.Count(pipelines, PipelineCatalogStatus.Failed, NoFleet));
        Assert.Equal(1, PipelineCatalogView.Count(pipelines, PipelineCatalogStatus.NeverRun, NoFleet));
        Assert.Equal(0, PipelineCatalogView.Count(pipelines, PipelineCatalogStatus.Running, NoFleet));
    }

    [Fact]
    public void Statuses_ShowTheFleetStatesOnlyWithFleetData()
    {
        Assert.DoesNotContain(PipelineCatalogStatus.Outdated, PipelineCatalogView.Statuses(fleetAvailable: false));
        Assert.Equal(
            [PipelineCatalogStatus.Failed, PipelineCatalogStatus.Running, PipelineCatalogStatus.NeverRun,
             PipelineCatalogStatus.Outdated, PipelineCatalogStatus.Current, PipelineCatalogStatus.OffCatalog],
            PipelineCatalogView.Statuses(fleetAvailable: true));
    }

    [Fact]
    public void OrderFavoritesFirst_PutsStarredRowsFirst_ThenProjectThenName()
    {
        var pipelines = new[]
        {
            new PipelineDependencyDto { Id = 1, Name = "build", ProjectName = "Beta" },
            new PipelineDependencyDto { Id = 2, Name = "deploy", ProjectName = "Alpha" },
            new PipelineDependencyDto { Id = 3, Name = "zz-release", ProjectName = "Beta" },
            new PipelineDependencyDto { Id = 4, Name = "audit", ProjectName = "Alpha" }
        };

        var ordered = PipelineCatalogView.OrderFavoritesFirst(pipelines, new HashSet<int> { 3, 1 });

        Assert.Equal([1, 3, 4, 2], ordered.Select(pipeline => pipeline.Id));
    }
}
