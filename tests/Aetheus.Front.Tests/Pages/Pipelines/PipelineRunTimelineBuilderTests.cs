// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Pipelines;
using static Aetheus.Front.Components.Pipelines.PipelineRunTimelineBuilder;

namespace Aetheus.Front.Tests.Pages.Pipelines;

/// <summary>
/// The run Progress view is a flat, ordered list: group headers, stages, sub-steps and release
/// milestones interleaved in execution order. Getting the order or the grouping flag wrong puts a
/// release under the wrong stage, which is what an operator reads to know what shipped.
/// </summary>
public class PipelineRunTimelineBuilderTests
{
    private static PipelineStepRunDto Step(string name, string? group = null, int? releaseId = null)
    {
        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
        if (releaseId is { } id) outputs["RELEASE_ID"] = id.ToString();
        return new PipelineStepRunDto { StepName = name, GroupName = group, OutputVariables = outputs };
    }

    private static StageViewModel Stage(string name, string? group = null, params PipelineStepRunDto[] steps) =>
        new() { Name = name, DisplayName = name, GroupName = group, Steps = [.. steps] };

    private static PipelineRunDto Run() => new() { Id = 1 };

    private static ReleaseDto Release(int id) => new() { Id = id, Version = $"1.0.{id}" };

    [Fact]
    public void NoRun_ProducesAnEmptyTimeline()
    {
        Assert.Empty(Build(null, [Stage("build", null, Step("s"))], [Release(1)]));
    }

    [Fact]
    public void SingleStepStage_IsOneStageNode_NotAStagePlusASubStep()
    {
        // A one-step stage is drawn as a single row; emitting a sub-step under it duplicates the line.
        var nodes = Build(Run(), [Stage("build", null, Step("compile"))], []);

        var node = Assert.Single(nodes);
        Assert.Equal(TimelineNodeKind.Stage, node.Kind);
        Assert.Equal("compile", node.Step!.StepName);
    }

    [Fact]
    public void MultiStepStage_IsAStageHeaderFollowedByItsSubSteps()
    {
        var nodes = Build(Run(), [Stage("test", null, Step("unit"), Step("e2e"))], []);

        Assert.Equal(
            [TimelineNodeKind.Stage, TimelineNodeKind.SubStep, TimelineNodeKind.SubStep],
            nodes.Select(n => n.Kind));
        Assert.Null(nodes[0].Step);
        Assert.Equal(["unit", "e2e"], nodes.Skip(1).Select(n => n.Step!.StepName));
    }

    [Fact]
    public void AGroupHeaderIsEmittedOnceWhenTheGroupChanges()
    {
        var nodes = Build(Run(), [
            Stage("a", "quality", Step("s1")),
            Stage("b", "quality", Step("s2")),
            Stage("c", "deploy", Step("s3"))
        ], []);

        var headers = nodes.Where(n => n.Kind == TimelineNodeKind.GroupHeader).ToList();
        Assert.Equal(["quality", "deploy"], headers.Select(h => h.GroupName));
    }

    [Fact]
    public void StagesInsideAGroup_AreFlaggedInGroup()
    {
        var nodes = Build(Run(), [
            Stage("a", "quality", Step("s1")),
            Stage("b", null, Step("s2"))
        ], []);

        Assert.True(nodes.Single(n => n.Kind == TimelineNodeKind.Stage && n.Stage!.Name == "a").InGroup);
        Assert.False(nodes.Single(n => n.Kind == TimelineNodeKind.Stage && n.Stage!.Name == "b").InGroup);
    }

    [Fact]
    public void ReleaseProducedByAStep_IsInsertedRightAfterThatStep()
    {
        var nodes = Build(
            Run(),
            [Stage("publish", null, Step("push", releaseId: 7)), Stage("after", null, Step("later"))],
            [Release(7)]);

        Assert.Equal(TimelineNodeKind.Release, nodes[1].Kind);
        Assert.Equal(7, nodes[1].Release!.Id);
        Assert.Equal("later", nodes[2].Step!.StepName);
    }

    [Fact]
    public void ReleaseWithNoProducingStep_ClosesTheTimeline()
    {
        var nodes = Build(Run(), [Stage("build", null, Step("compile"))], [Release(42)]);

        Assert.Equal(TimelineNodeKind.Release, nodes[^1].Kind);
        Assert.Equal(42, nodes[^1].Release!.Id);
    }

    [Fact]
    public void TheSameReleaseIsNeverEmittedTwice()
    {
        // Two steps can both carry RELEASE_ID=7 (retry, matrix leg). The milestone must appear once.
        var nodes = Build(
            Run(),
            [Stage("publish", null, Step("push-a", releaseId: 7), Step("push-b", releaseId: 7))],
            [Release(7)]);

        Assert.Single(nodes, n => n.Kind == TimelineNodeKind.Release);
    }

    [Fact]
    public void ReleaseIdPointingAtAnUnknownRelease_EmitsNoMilestone()
    {
        var nodes = Build(Run(), [Stage("publish", null, Step("push", releaseId: 999))], [Release(7)]);

        // 7 is unknown to any step, so it closes the timeline; 999 resolves to nothing.
        Assert.Single(nodes, n => n.Kind == TimelineNodeKind.Release);
        Assert.Equal(7, nodes.Single(n => n.Kind == TimelineNodeKind.Release).Release!.Id);
    }

    [Fact]
    public void UnparsableReleaseId_IsIgnoredRatherThanThrowing()
    {
        var step = new PipelineStepRunDto
        {
            StepName = "push",
            OutputVariables = new Dictionary<string, string>(StringComparer.Ordinal) { ["RELEASE_ID"] = "not-a-number" }
        };

        var nodes = Build(Run(), [Stage("publish", null, step)], []);

        Assert.Single(nodes);
        Assert.Equal(TimelineNodeKind.Stage, nodes[0].Kind);
    }

    [Fact]
    public void ReleaseEmittedInsideAGroup_InheritsTheGroupFlagFromItsStep()
    {
        var nodes = Build(
            Run(),
            [Stage("publish", "deploy", Step("push", group: "deploy", releaseId: 3))],
            [Release(3)]);

        Assert.True(nodes.Single(n => n.Kind == TimelineNodeKind.Release).InGroup);
    }
}
