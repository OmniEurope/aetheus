// SPDX-License-Identifier: EUPL-1.2
using Bunit;

namespace Aetheus.Front.Tests.Shared;

public class PipelineRunHistoryTests : BunitContext
{
    public PipelineRunHistoryTests() => BunitTestHelper.RegisterServices(this);

    [Fact]
    public void RendersOnlyFiveRuns_OldestToNewest_WithContextLinks()
    {
        var runs = Enumerable.Range(1, 6)
            .Reverse()
            .Select(id => new PipelineRunSummaryDto
            {
                Id = id,
                Status = id == 6 ? PipelineStatus.WaitingForApproval : PipelineStatus.Success,
                StartedAt = new DateTime(2026, 7, 1, 0, id, 0)
            })
            .ToList();
        var cut = Render<PipelineRunHistory>(parameters => parameters
            .Add(component => component.Runs, runs)
            .Add(component => component.ProjectId, 8));

        var dots = cut.FindAll("a.pipeline-run-dot");
        Assert.Equal(5, dots.Count);
        Assert.Equal("/pipelines/runs/2?projectId=8", dots[0].GetAttribute("href"));
        Assert.Equal("/pipelines/runs/6?projectId=8", dots[^1].GetAttribute("href"));
        Assert.Contains("pipeline-run-dot-waitingforapproval", dots[^1].ClassList);
        Assert.All(dots, dot => Assert.NotNull(dot.GetAttribute("aria-label")));
    }

    [Fact]
    public void EmptyHistory_ShowsNoRunsLabel()
    {
        var cut = Render<PipelineRunHistory>(parameters => parameters
            .Add(component => component.Runs, Array.Empty<PipelineRunSummaryDto>()));

        Assert.Contains("NoRuns", cut.Markup);
        Assert.Empty(cut.FindAll(".pipeline-run-dot"));
    }
}
