// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Pipelines;

/// <summary>
/// Guards the recursive run timeline refactor (A1-A4): stage grouping (<c>RunStageBuilder</c>), the
/// trigger-node lazy-load/expand, and the two failure paths that previously had no coverage - a fetch
/// error and a malformed child body (the latter used to escape as an uncaught <c>JsonException</c>).
/// </summary>
public class RunTimelineTreeTests : BunitContext
{
    public RunTimelineTreeTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private readonly BunitTestHelper.TestHandler _handler;

    private static PipelineStepRunDto Step(int id, string stage, string name, int? triggeredRunId = null) => new()
    {
        Id = id,
        StageName = stage,
        StepName = name,
        Status = TaskExecutionStatus.Success,
        TriggeredRunId = triggeredRunId
    };

    [Fact]
    public void Build_GroupsStepsByStage_RendersStagesAndSteps()
    {
        var run = new PipelineRunDto
        {
            Id = 1,
            Status = PipelineStatus.Success,
            Steps =
            [
                Step(1, "build", "compile"),
                Step(2, "build", "test"),
                Step(3, "deploy", "ship")
            ]
        };

        var cut = Render<RunTimelineTree>(p => p.Add(x => x.Run, run));

        // Multi-step stage renders a header (display name) + each step; single-step stage renders under
        // the stage display name. This exercises RunStageBuilder.Build grouping behaviourally.
        Assert.Contains("build", cut.Markup);
        Assert.Contains("compile", cut.Markup);
        Assert.Contains("test", cut.Markup);
        Assert.Contains("deploy", cut.Markup);
    }

    [Fact]
    public void AutoExpand_CyclicChildGraph_DoesNotRecurseForever()
    {
        var run1 = new PipelineRunDto
        {
            Id = 1,
            Status = PipelineStatus.Running,
            Steps = [Step(1, "orchestrate", "trigger-two", triggeredRunId: 2)]
        };
        var run2 = new PipelineRunDto
        {
            Id = 2,
            Status = PipelineStatus.Running,
            Steps = [Step(2, "orchestrate", "trigger-one", triggeredRunId: 1)]
        };

        var cut = Render<RunTimelineTree>(parameters => parameters
            .Add(component => component.Run, run1)
            .Add(component => component.SelectedStepId, 999)
            .Add(component => component.PreloadedChildren,
                new Dictionary<int, PipelineRunDto> { [1] = run1, [2] = run2 }));

        Assert.Contains("trigger-two", cut.Markup);
    }

    [Fact]
    public void TriggerStep_Expand_LazyLoadsAndNestsChildRun()
    {
        _handler.SetJsonResponse("api/pipelines/runs/99", new PipelineRunDto
        {
            Id = 99,
            Status = PipelineStatus.Success,
            Steps = [Step(10, "childstage", "child-a"), Step(11, "childstage", "child-b")]
        });

        var parent = new PipelineRunDto
        {
            Id = 1,
            Status = PipelineStatus.Running,
            Steps = [Step(1, "orchestrate", "trigger-child", triggeredRunId: 99)]
        };

        var cut = Render<RunTimelineTree>(p => p.Add(x => x.Run, parent));

        // Not fetched until expanded.
        Assert.DoesNotContain("child-a", cut.Markup);
        Assert.DoesNotContain("api/pipelines/runs/99", _handler.Requests.Select(r => r.Url).DefaultIfEmpty(""));

        cut.Find(".run-tl-trigger").Click();
        cut.WaitForState(() => cut.Markup.Contains("child-a"));

        Assert.Contains("child-a", cut.Markup);
        Assert.Contains("child-b", cut.Markup);
        Assert.Single(_handler.Requests, r => r.Url.Contains("pipelines/runs/99"));
    }

    [Fact]
    public void TriggerStep_Collapse_HidesChildren()
    {
        _handler.SetJsonResponse("api/pipelines/runs/99", new PipelineRunDto
        {
            Id = 99,
            Status = PipelineStatus.Success,
            Steps = [Step(10, "childstage", "child-a"), Step(11, "childstage", "child-b")]
        });
        var parent = new PipelineRunDto
        {
            Id = 1,
            Status = PipelineStatus.Success,
            Steps = [Step(1, "orchestrate", "trigger-child", triggeredRunId: 99)]
        };

        var cut = Render<RunTimelineTree>(p => p.Add(x => x.Run, parent));
        cut.Find(".run-tl-trigger").Click();
        cut.WaitForState(() => cut.Markup.Contains("child-a"));

        cut.Find(".run-tl-trigger").Click();
        cut.WaitForState(() => !cut.Markup.Contains("child-a"));
        Assert.DoesNotContain("child-a", cut.Markup);
    }

    [Fact]
    public void TriggerStep_ChildFetchFails_ShowsNotFound_NoCrash()
    {
        _handler.SetResponse("api/pipelines/runs/99", System.Net.HttpStatusCode.InternalServerError);
        var parent = new PipelineRunDto
        {
            Id = 1,
            Status = PipelineStatus.Running,
            Steps = [Step(1, "orchestrate", "trigger-child", triggeredRunId: 99)]
        };

        var cut = Render<RunTimelineTree>(p => p.Add(x => x.Run, parent));
        cut.Find(".run-tl-trigger").Click();
        cut.WaitForState(() => cut.Markup.Contains("ChildRunNotFound"));

        Assert.Contains("ChildRunNotFound", cut.Markup);
    }

    [Fact]
    public void TriggerStep_MalformedChildJson_DoesNotThrow_ShowsNotFound()
    {
        // Regression guard: a malformed body used to surface an uncaught JsonException and tear down the
        // whole timeline. It must now degrade to the "child run not found" note like any fetch failure.
        _handler.SetRawResponse("api/pipelines/runs/99", "{ this is not valid json");
        var parent = new PipelineRunDto
        {
            Id = 1,
            Status = PipelineStatus.Running,
            Steps = [Step(1, "orchestrate", "trigger-child", triggeredRunId: 99)]
        };

        var cut = Render<RunTimelineTree>(p => p.Add(x => x.Run, parent));

        var ex = Record.Exception(() =>
        {
            cut.Find(".run-tl-trigger").Click();
            cut.WaitForState(() => cut.Markup.Contains("ChildRunNotFound"));
        });

        Assert.Null(ex);
        Assert.Contains("ChildRunNotFound", cut.Markup);
    }

    [Fact]
    public void TriggerStep_LiveChild_RefetchedOnReexpand()
    {
        // A still-running child keeps progressing, so its cached snapshot goes stale: re-expanding must
        // re-fetch it (only a terminal child is safe to keep from the first load).
        // Two steps so the child stage renders its step NAMES (a single-step stage renders the stage
        // display name instead). Status Running = non-terminal, so the cached snapshot is stale on re-expand.
        _handler.SetJsonResponse("api/pipelines/runs/99", new PipelineRunDto
        {
            Id = 99,
            Status = PipelineStatus.Running,
            Steps = [Step(10, "childstage", "child-a"), Step(11, "childstage", "child-b")]
        });
        var parent = new PipelineRunDto
        {
            Id = 1,
            Status = PipelineStatus.Running,
            Steps = [Step(1, "orchestrate", "trigger-child", triggeredRunId: 99)]
        };

        int Fetches() => _handler.Requests.Count(r => r.Url.Contains("pipelines/runs/99"));

        var cut = Render<RunTimelineTree>(p => p.Add(x => x.Run, parent));
        cut.Find(".run-tl-trigger").Click();   // expand → fetch #1
        cut.WaitForState(() => cut.Markup.Contains("child-a"));
        Assert.Equal(1, Fetches());

        cut.Find(".run-tl-trigger").Click();   // collapse (no fetch)
        cut.WaitForState(() => !cut.Markup.Contains("child-a"));

        cut.Find(".run-tl-trigger").Click();   // re-expand → fetch #2 (non-terminal, stale)
        cut.WaitForState(() => Fetches() == 2);
        Assert.Equal(2, Fetches());
    }

    [Fact]
    public void RunningTask_ShowsOnlyItsDurationFromPreviousRun()
    {
        var now = new DateTime(2026, 7, 16, 18, 0, 0);
        var current = new PipelineRunDto
        {
            Id = 20,
            PipelineId = 6,
            Status = PipelineStatus.Running,
            Steps =
            [
                new PipelineStepRunDto { Id = 1, StageName = "build", StepName = "compile", Status = TaskExecutionStatus.Success },
                new PipelineStepRunDto { Id = 2, StageName = "build", StepName = "test", Status = TaskExecutionStatus.Running, StartedAt = now }
            ]
        };
        var previous = new PipelineRunDto
        {
            Id = 19,
            PipelineId = 6,
            Status = PipelineStatus.Success,
            Steps =
            [
                new PipelineStepRunDto { Id = 3, StageName = "build", StepName = "compile", Status = TaskExecutionStatus.Success, StartedAt = now.AddMinutes(-4), CompletedAt = now.AddMinutes(-3) },
                new PipelineStepRunDto { Id = 4, StageName = "build", StepName = "test", Status = TaskExecutionStatus.Success, StartedAt = now.AddMinutes(-2), CompletedAt = now.AddSeconds(-30) }
            ]
        };

        var cut = Render<RunTimelineTree>(parameters => parameters
            .Add(component => component.Run, current)
            .Add(component => component.PreviousRuns, new Dictionary<int, PipelineRunDto> { [current.Id] = previous }));

        var marker = Assert.Single(cut.FindAll(".run-tl-prev-dur"));
        Assert.Contains("PreviousRunDuration", marker.TextContent);
        Assert.Equal("test", marker.ParentElement!.QuerySelector(".run-tl-name")!.TextContent);
    }

    [Fact]
    public void OpenChildRunLinks_AreNativeLinksWithProjectContext()
    {
        var parent = new PipelineRunDto
        {
            Id = 1,
            Status = PipelineStatus.Running,
            Steps = [Step(1, "orchestrate", "Deploy (child)", triggeredRunId: 99)]
        };

        var cut = Render<RunTimelineTree>(parameters => parameters
            .Add(component => component.Run, parent)
            .Add(component => component.ProjectId, 8));

        var links = cut.FindAll("a[title='OpenChildRun']");
        Assert.Equal(2, links.Count);
        Assert.All(links, link => Assert.Equal("/pipelines/runs/99?projectId=8", link.GetAttribute("href")));

        Assert.All(links, link => Assert.Null(link.GetAttribute("onclick")));
    }
}
