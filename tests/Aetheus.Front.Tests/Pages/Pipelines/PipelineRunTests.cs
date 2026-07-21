// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages;
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Radzen;

namespace Aetheus.Front.Tests.Pages;

public class PipelineRunTests : BunitContext
{
    public PipelineRunTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        _handler.SetJsonResponse(HttpMethod.Get, "api/releases/by-run/", new List<ReleaseDto>());
        _handler.SetResponse(HttpMethod.Get, "/source", System.Net.HttpStatusCode.NoContent);
        _handler.SetPaginatedJsonResponse<PipelineRunDto>(
            HttpMethod.Get,
            "/runs?pageSize=50",
            []);
    }

    private readonly BunitTestHelper.TestHandler _handler;

    [Fact]
    public void Renders_RunDetails()
    {
        _handler.SetJsonResponse("api/pipelines/runs/1", new PipelineRunDto
        {
            Id = 1,
            PipelineId = 5,
            PipelineName = "CI Build",
            Status = PipelineStatus.Success,
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            CompletedAt = DateTime.UtcNow,
            Steps =
            [
                new PipelineStepRunDto
                {
                    Id = 1,
                    StepName = "Build",
                    StageName = "build",
                    Status = TaskExecutionStatus.Success,
                    StartedAt = DateTime.UtcNow.AddMinutes(-5),
                    CompletedAt = DateTime.UtcNow
                }
            ]
        });

        var cut = Render<PipelineRun>(p => p.Add(x => x.RunId, 1));
        cut.WaitForState(() => cut.Markup.Contains("CI Build"), TimeSpan.FromSeconds(2));

        Assert.Contains("CI Build", cut.Markup);
        Assert.Contains("Build", cut.Markup);
    }

    [Fact]
    public void ChildRun_RendersLinkedParentPipelineTile()
    {
        _handler.SetPaginatedJsonResponse("api/git/repos?projectId=8", new List<GitLightRepoDto>());
        _handler.SetJsonResponse("api/pipelines/runs/7", new PipelineRunDto
        {
            Id = 7,
            PipelineId = 5,
            ProjectId = 8,
            PipelineName = "child-ci",
            Status = PipelineStatus.Running,
            StartedAt = DateTime.UtcNow,
            ResolvedVariables = new Dictionary<string, string>
            {
                ["UPSTREAM_PIPELINE"] = "release-parent",
                ["UPSTREAM_RUN_ID"] = "42",
                ["UPSTREAM_CHAIN"] = "8"
            }
        });

        var cut = Render<PipelineRun>(parameters => parameters.Add(component => component.RunId, 7));
        cut.WaitForState(() => cut.Markup.Contains("run-parent-pipeline-tile"), TimeSpan.FromSeconds(2));

        var tile = cut.Find("a.run-parent-pipeline-tile");
        Assert.Equal("/pipelines/runs/42?projectId=8", tile.GetAttribute("href"));
        Assert.Contains("ParentPipeline", tile.TextContent);
        Assert.Contains("release-parent", tile.TextContent);
        Assert.Contains("Run #42", tile.TextContent);
    }

    [Fact]
    public void RunIdParameterChange_ReloadsDisplayedRun()
    {
        _handler.SetJsonResponse("api/pipelines/runs/317", new PipelineRunDto
        {
            Id = 317,
            PipelineId = 10,
            PipelineName = "parent-release",
            Status = PipelineStatus.Success,
            StartedAt = DateTime.UtcNow.AddMinutes(-2),
            CompletedAt = DateTime.UtcNow.AddMinutes(-1)
        });
        _handler.SetJsonResponse("api/pipelines/runs/318", new PipelineRunDto
        {
            Id = 318,
            PipelineId = 11,
            PipelineName = "child-ci",
            Status = PipelineStatus.Success,
            StartedAt = DateTime.UtcNow.AddMinutes(-1),
            CompletedAt = DateTime.UtcNow
        });

        var cut = Render<PipelineRun>(parameters => parameters.Add(component => component.RunId, 317));
        cut.WaitForState(() => cut.Markup.Contains("parent-release"), TimeSpan.FromSeconds(2));

        cut.Render(parameters => parameters.Add(component => component.RunId, 318));
        cut.WaitForState(() => cut.Markup.Contains("child-ci"), TimeSpan.FromSeconds(2));

        Assert.Contains("Run #318", cut.Markup);
        Assert.Contains("child-ci", cut.Markup);
        Assert.DoesNotContain("parent-release", cut.Markup);
    }

    [Fact]
    public async Task ReloadTerminalRun_StopsDurationTimer()
    {
        _handler.SetJsonResponse("api/pipelines/runs/51", new PipelineRunDto
        {
            Id = 51,
            PipelineId = 5,
            PipelineName = "live",
            Status = PipelineStatus.Running,
            StartedAt = DateTime.UtcNow.AddMinutes(-1)
        });
        var cut = Render<PipelineRun>(p => p.Add(x => x.RunId, 51));
        cut.WaitForState(() => typeof(PipelineRun).GetField("_durationTimer",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance) is not null);
        _handler.SetJsonResponse("api/pipelines/runs/51", new PipelineRunDto
        {
            Id = 51,
            PipelineId = 5,
            PipelineName = "live",
            Status = PipelineStatus.Success,
            StartedAt = DateTime.UtcNow.AddMinutes(-1),
            CompletedAt = DateTime.UtcNow
        });
        var reload = typeof(PipelineRun).GetMethod("ReloadRunAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        await cut.InvokeAsync(async () => await (Task)reload.Invoke(cut.Instance, [])!);

        Assert.Null(typeof(PipelineRun).GetField("_durationTimer",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance));
    }

    [Fact]
    public void RepositoryTile_MatchesRunUrlInsteadOfTakingFirstProjectRepository()
    {
        _handler.SetJsonResponse("api/pipelines/runs/52", new PipelineRunDto
        {
            Id = 52,
            PipelineId = 6,
            ProjectId = 8,
            PipelineName = "build",
            ProjectName = "Project",
            RepositoryUrl = "https://git.example/acme/right.git",
            Status = PipelineStatus.Success,
            StartedAt = DateTime.UtcNow.AddMinutes(-1),
            CompletedAt = DateTime.UtcNow
        });
        _handler.SetResponse("api/pipelines/6/source", System.Net.HttpStatusCode.NoContent);
        _handler.SetPaginatedJsonResponse("api/git/repos", new List<GitLightRepoDto>
        {
            new() { Id = 11, ProjectId = 8, Name = "wrong", CloneUrl = "https://git.example/acme/wrong.git" },
            new() { Id = 22, ProjectId = 8, Name = "right", CloneUrl = "https://git.example/acme/right" }
        });

        var cut = Render<PipelineRun>(p => p.Add(x => x.RunId, 52));

        cut.WaitForAssertion(() => Assert.Contains("/git-repositories/22", cut.Markup));
        Assert.DoesNotContain("/git-repositories/11", cut.Markup);
        Assert.Equal(22, typeof(PipelineRun).GetField("_repoId",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance));
    }

    [Fact]
    public void Renders_LoadingState()
    {
        _handler.SetJsonResponse<PipelineRunDto?>("api/pipelines/runs/99", null);

        var cut = Render<PipelineRun>(p => p.Add(x => x.RunId, 99));
        // Null run data -> the page renders its "run not found" empty state, not the run details.
        cut.WaitForState(() => cut.Markup.Contains("RunNotFound"), TimeSpan.FromSeconds(2));
        Assert.Contains("RunNotFound", cut.Markup);
        Assert.DoesNotContain("pipeline-run-toolbar", cut.Markup);
    }

    [Fact]
    public void Groups_StepsByStage()
    {
        _handler.SetJsonResponse("api/pipelines/runs/2", new PipelineRunDto
        {
            Id = 2,
            PipelineName = "Deploy",
            Status = PipelineStatus.Running,
            StartedAt = DateTime.UtcNow,
            Steps =
            [
                new PipelineStepRunDto
                {
                    Id = 1, StepName = "Checkout", StageName = "prepare",
                    Status = TaskExecutionStatus.Success,
                    StartedAt = DateTime.UtcNow.AddMinutes(-3), CompletedAt = DateTime.UtcNow.AddMinutes(-2)
                },
                new PipelineStepRunDto
                {
                    Id = 2, StepName = "Build", StageName = "build",
                    Status = TaskExecutionStatus.Running,
                    StartedAt = DateTime.UtcNow.AddMinutes(-1)
                },
                new PipelineStepRunDto
                {
                    Id = 3, StepName = "Test", StageName = "build",
                    Status = TaskExecutionStatus.Pending
                }
            ]
        });

        var cut = Render<PipelineRun>(p => p.Add(x => x.RunId, 2));
        cut.WaitForState(() => cut.Markup.Contains("Deploy"), TimeSpan.FromSeconds(2));

        // Verify the real grouping contract on the computed stage model: the 3 steps collapse into 2
        // stages - "prepare" (1 step) and "build" (2 steps). (The timeline markup lives behind a Radzen
        // tab; asserting _stages is a deterministic check of the grouping logic itself.)
        var byName = StageStepCounts(cut);
        Assert.Equal(2, byName.Count);
        Assert.Equal(1, byName["prepare"]);
        Assert.Equal(2, byName["build"]);
    }

    // Reflects over the component's computed _stages (List of internal StageViewModel) to expose each
    // stage's Name and step count - the grouping contract without depending on tab/markup rendering.
    private static Dictionary<string, int> StageStepCounts(IRenderedComponent<PipelineRun> cut)
    {
        var stages = (System.Collections.IEnumerable)typeof(PipelineRun)
            .GetField("_stages", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        return stages.Cast<object>().ToDictionary(
            s => (string)s.GetType().GetProperty("Name")!.GetValue(s)!,
            s => ((System.Collections.ICollection)s.GetType().GetProperty("Steps")!.GetValue(s)!).Count);
    }

    [Fact]
    public void MultiStage_ShowsDurations()
    {
        _handler.SetJsonResponse("api/pipelines/runs/3", new PipelineRunDto
        {
            Id = 3,
            PipelineName = "Full",
            Status = PipelineStatus.Success,
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            CompletedAt = DateTime.UtcNow,
            Steps =
            [
                new PipelineStepRunDto
                {
                    Id = 1, StepName = "A", StageName = "s1",
                    Status = TaskExecutionStatus.Success,
                    StartedAt = DateTime.UtcNow.AddMinutes(-10),
                    CompletedAt = DateTime.UtcNow.AddMinutes(-5)
                },
                new PipelineStepRunDto
                {
                    Id = 2, StepName = "B", StageName = "s2",
                    Status = TaskExecutionStatus.Success,
                    StartedAt = DateTime.UtcNow.AddMinutes(-5),
                    CompletedAt = DateTime.UtcNow
                }
            ]
        });

        var cut = Render<PipelineRun>(p => p.Add(x => x.RunId, 3));
        cut.WaitForState(() => cut.Markup.Contains("Full"), TimeSpan.FromSeconds(2));

        // Each stage step spans 5 minutes, so a rendered "5m ..s" duration must appear in the timeline.
        Assert.Contains("run-tl-dur", cut.Markup);
        Assert.Matches(@"\d+m \d+s", cut.Markup);
    }

    // --- Static method tests ---

    [Fact]
    public void FormatDuration_NullStart_ReturnsEmpty()
    {
        var result = PipelineRunFormatting.FormatDuration(null, null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void FormatDuration_WithValues_ReturnsFormatted()
    {
        var started = DateTime.UtcNow.AddMinutes(-2).AddSeconds(-30);
        var completed = DateTime.UtcNow;
        var result = PipelineRunFormatting.FormatDuration(started, completed);
        Assert.Contains("m", result);
    }

    [Fact]
    public void FormatDuration_ShortDuration_ShowsSeconds()
    {
        var started = DateTime.UtcNow;
        var completed = started.AddSeconds(45);
        var result = PipelineRunFormatting.FormatDuration(started, completed);
        Assert.Equal("45s", result);
    }

    [Theory]
    [InlineData(PipelineStatus.Success, BadgeStyle.Success)]
    [InlineData(PipelineStatus.Failed, BadgeStyle.Danger)]
    [InlineData(PipelineStatus.Running, BadgeStyle.Info)]
    public void GetRunBadge_ReturnsExpectedStyle(PipelineStatus status, BadgeStyle expected)
    {
        var result = PipelineRunFormatting.GetRunBadge(status);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(TaskExecutionStatus.Success, BadgeStyle.Success)]
    [InlineData(TaskExecutionStatus.Failed, BadgeStyle.Danger)]
    [InlineData(TaskExecutionStatus.Running, BadgeStyle.Info)]
    [InlineData(TaskExecutionStatus.Pending, BadgeStyle.Light)]
    public void GetStepBadge_ReturnsExpectedStyle(TaskExecutionStatus status, BadgeStyle expected)
    {
        var result = PipelineRunFormatting.GetStepBadge(status);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void MatrixGroupKey_ReturnsCorrectFormat()
    {
        var result = PipelineRunFormatting.MatrixGroupKey("build", "compile");
        Assert.Equal("build::compile", result);
    }

    // --- SelectStep ---

    [Fact]
    public void SelectStep_SetsSelectedStep()
    {
        _handler.SetJsonResponse("api/pipelines/runs/4", new PipelineRunDto
        {
            Id = 4,
            PipelineName = "Toggle Test",
            Status = PipelineStatus.Success,
            Steps =
            [
                new PipelineStepRunDto { Id = 1, StepName = "A", StageName = "s1", Status = TaskExecutionStatus.Success }
            ]
        });

        var cut = Render<PipelineRun>(p => p.Add(x => x.RunId, 4));
        cut.WaitForState(() => cut.Markup.Contains("Toggle Test"), TimeSpan.FromSeconds(2));

        var selected = typeof(PipelineRun).GetField("_selectedStep", BindingFlags.NonPublic | BindingFlags.Instance)!;
        // Auto-selects the last completed step
        Assert.NotNull(selected.GetValue(cut.Instance));
    }

    // --- ToggleMatrixGroup ---

    [Fact]
    public void ToggleMatrixGroup_AddsAndRemoves()
    {
        _handler.SetJsonResponse("api/pipelines/runs/5", new PipelineRunDto
        {
            Id = 5,
            PipelineName = "Matrix",
            Status = PipelineStatus.Success,
            Steps = [new PipelineStepRunDto { Id = 1, StepName = "A", StageName = "s1", Status = TaskExecutionStatus.Success }]
        });

        var cut = Render<PipelineRun>(p => p.Add(x => x.RunId, 5));
        cut.WaitForState(() => cut.Markup.Contains("Matrix"), TimeSpan.FromSeconds(2));

        var method = typeof(PipelineRun).GetMethod("ToggleMatrixGroup", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(cut.Instance, ["s1::A"]);

        var groups = (HashSet<string>)typeof(PipelineRun).GetField("_expandedMatrixGroups", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Contains("s1::A", groups);

        method.Invoke(cut.Instance, ["s1::A"]);
        Assert.DoesNotContain("s1::A", groups);
    }

    // --- GetAggregateStatus ---

    [Fact]
    public void GetAggregateStatus_AllSuccess_ReturnsSuccess()
    {
        var steps = new List<PipelineStepRunDto>
        {
            new() { Status = TaskExecutionStatus.Success },
            new() { Status = TaskExecutionStatus.Success }
        };
        var result = PipelineRunFormatting.GetAggregateStatus(steps);
        Assert.Equal(TaskExecutionStatus.Success, result);
    }

    [Fact]
    public void GetAggregateStatus_OneFailed_ReturnsFailed()
    {
        var steps = new List<PipelineStepRunDto>
        {
            new() { Status = TaskExecutionStatus.Success },
            new() { Status = TaskExecutionStatus.Failed }
        };
        var result = PipelineRunFormatting.GetAggregateStatus(steps);
        Assert.Equal(TaskExecutionStatus.Failed, result);
    }

    [Fact]
    public void GetAggregateStatus_OneRunning_ReturnsRunning()
    {
        var steps = new List<PipelineStepRunDto>
        {
            new() { Status = TaskExecutionStatus.Success },
            new() { Status = TaskExecutionStatus.Running }
        };
        var result = PipelineRunFormatting.GetAggregateStatus(steps);
        Assert.Equal(TaskExecutionStatus.Running, result);
    }

    [Fact]
    public void GetAggregateStatus_AllPending_ReturnsPending()
    {
        var steps = new List<PipelineStepRunDto>
        {
            new() { Status = TaskExecutionStatus.Pending },
            new() { Status = TaskExecutionStatus.Pending }
        };
        var result = PipelineRunFormatting.GetAggregateStatus(steps);
        Assert.Equal(TaskExecutionStatus.Pending, result);
    }

    // --- Failed run rendering ---

    [Fact]
    public void Renders_FailedRun()
    {
        _handler.SetJsonResponse("api/pipelines/runs/6", new PipelineRunDto
        {
            Id = 6,
            PipelineName = "Failed Pipeline",
            Status = PipelineStatus.Failed,
            StartedAt = DateTime.UtcNow.AddMinutes(-2),
            CompletedAt = DateTime.UtcNow,
            Steps =
            [
                new PipelineStepRunDto { Id = 1, StepName = "Build", StageName = "build", Status = TaskExecutionStatus.Success },
                new PipelineStepRunDto { Id = 2, StepName = "Deploy", StageName = "deploy", Status = TaskExecutionStatus.Failed }
            ]
        });

        var cut = Render<PipelineRun>(p => p.Add(x => x.RunId, 6));
        cut.WaitForState(() => cut.Markup.Contains("Failed Pipeline"), TimeSpan.FromSeconds(2));
        Assert.Contains("Failed Pipeline", cut.Markup);
    }

    [Fact]
    public void SelectStep_AutoSelectsLastCompleted()
    {
        _handler.SetJsonResponse("api/pipelines/runs/10", new PipelineRunDto
        {
            Id = 10,
            PipelineName = "Toggle Test",
            Status = PipelineStatus.Success,
            Steps =
            [
                new PipelineStepRunDto { Id = 1, StepName = "s1", StageName = "build", Status = TaskExecutionStatus.Success },
                new PipelineStepRunDto { Id = 2, StepName = "s2", StageName = "deploy", Status = TaskExecutionStatus.Success }
            ]
        });

        var cut = Render<PipelineRun>(p => p.Add(x => x.RunId, 10));
        cut.WaitForState(() => cut.Markup.Contains("Toggle Test"), TimeSpan.FromSeconds(2));

        var selected = typeof(PipelineRun).GetField("_selectedStep", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var step = selected.GetValue(cut.Instance) as PipelineStepRunDto;
        // Should auto-select the last completed step (s2)
        Assert.NotNull(step);
        Assert.Equal("s2", step!.StepName);
    }

    [Fact]
    public void MatrixGroupKey_CombinesStageName()
    {
        var result = PipelineRunFormatting.MatrixGroupKey("build", "test-step");
        Assert.Equal("build::test-step", result);
    }

    [Fact]
    public void Renders_RunWithMultipleStages()
    {
        _handler.SetJsonResponse("api/pipelines/runs/20", new PipelineRunDto
        {
            Id = 20,
            PipelineName = "Multi Stage",
            Status = PipelineStatus.Running,
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            Steps =
            [
                new PipelineStepRunDto { Id = 1, StepName = "Compile", StageName = "build", Status = TaskExecutionStatus.Success, StartedAt = DateTime.UtcNow.AddMinutes(-5), CompletedAt = DateTime.UtcNow.AddMinutes(-3) },
                new PipelineStepRunDto { Id = 2, StepName = "UnitTest", StageName = "test", Status = TaskExecutionStatus.Running, StartedAt = DateTime.UtcNow.AddMinutes(-2) },
                new PipelineStepRunDto { Id = 3, StepName = "Deploy", StageName = "deploy", Status = TaskExecutionStatus.Pending }
            ]
        });

        var cut = Render<PipelineRun>(p => p.Add(x => x.RunId, 20));
        cut.WaitForState(() => cut.Markup.Contains("Multi Stage"), TimeSpan.FromSeconds(2));
        Assert.Contains("Multi Stage", cut.Markup);
        // All three stages (build/test/deploy) must be grouped from the steps.
        var byName = StageStepCounts(cut);
        Assert.Contains("build", byName.Keys);
        Assert.Contains("test", byName.Keys);
        Assert.Contains("deploy", byName.Keys);
    }
}
