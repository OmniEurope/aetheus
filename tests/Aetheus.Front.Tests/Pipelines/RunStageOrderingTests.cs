// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Pipelines;

namespace Aetheus.Front.Tests.Pipelines;

/// <summary>
/// How a run reads. Two defects lived here and both showed the same way to a user: a stage turning
/// green above one that had not finished, and a stage that would never start sitting in "pending"
/// for the rest of the run.
/// </summary>
public sealed class RunStageOrderingTests
{
    private static PipelineStepRunDto Step(
        int id, string stage, TaskExecutionStatus status = TaskExecutionStatus.Success,
        int? depth = null, string? skipped = null, bool isSystem = false) => new()
        {
            Id = id,
            StageName = stage,
            StepName = $"step {id}",
            Status = status,
            StageDepth = depth,
            SkippedCondition = skipped,
            IsSystem = isSystem
        };

    private static PipelineRunDto Run(params PipelineStepRunDto[] steps) => new() { Id = 1, Steps = [.. steps] };

    [Fact]
    public void StagesAreOrderedByDependencyDepthBeforeDefinitionOrder()
    {
        // Declared Build, Publish, Test: Test only waits for Build, so it is concurrent with Publish
        // and belongs beside it, not after it.
        var stages = RunStageBuilder.Build(Run(
            Step(1, "Build", depth: 0),
            Step(2, "Publish", depth: 2),
            Step(3, "Test", depth: 1)));

        Assert.Equal(["Build", "Test", "Publish"], stages.Select(stage => stage.Name));
    }

    [Fact]
    public void StagesAtTheSameDepthKeepTheirDefinitionOrder()
    {
        var stages = RunStageBuilder.Build(Run(
            Step(1, "Alpha", depth: 1),
            Step(2, "Beta", depth: 1),
            Step(3, "Gamma", depth: 1)));

        Assert.Equal(["Alpha", "Beta", "Gamma"], stages.Select(stage => stage.Name));
    }

    [Fact]
    public void AnUnknownDepthFallsBackToDefinitionOrderAfterTheGraph()
    {
        // A run with no usable snapshot must not be reordered into a plausible-looking wrong shape.
        var stages = RunStageBuilder.Build(Run(
            Step(1, "Nograph"),
            Step(2, "Graphed", depth: 3)));

        Assert.Equal(["Graphed", "Nograph"], stages.Select(stage => stage.Name));
    }

    [Fact]
    public void TheInjectedSystemStagesKeepTheEndsTheLauncherGaveThem()
    {
        // System:Prepare and System:Cleanup are not in the YAML, so they carry no depth. Sorting them
        // with the other depthless stages sent the clone that runs FIRST to the bottom of the page.
        var stages = RunStageBuilder.Build(Run(
            Step(1, "System:Prepare", isSystem: true),
            Step(2, "Compile", depth: 0),
            Step(3, "Publish", depth: 1),
            Step(4, "System:Cleanup", isSystem: true)));

        Assert.Equal(
            ["System:Prepare", "Compile", "Publish", "System:Cleanup"],
            stages.Select(stage => stage.Name));
    }

    [Fact]
    public void AStageCancelledByItsConditionIsTerminalRatherThanPending()
    {
        var stages = RunStageBuilder.Build(Run(
            Step(1, "Optional", TaskExecutionStatus.Cancelled, skipped: "eq(variables['PROFILE'], 'full')")));

        var stage = Assert.Single(stages);
        Assert.Equal(TaskExecutionStatus.Cancelled, stage.Status);
        Assert.Equal("eq(variables['PROFILE'], 'full')", stage.SkippedCondition);
    }

    [Fact]
    public void AStageCancelledWithoutAConditionClaimsNoCondition()
    {
        // Cancelled after an upstream failure is not "skipped by a condition", and saying so would
        // invent a reason the run never recorded.
        var stages = RunStageBuilder.Build(Run(Step(1, "Downstream", TaskExecutionStatus.Cancelled)));

        var stage = Assert.Single(stages);
        Assert.Equal(TaskExecutionStatus.Cancelled, stage.Status);
        Assert.Null(stage.SkippedCondition);
    }

    [Fact]
    public void APartlyCancelledStageIsStillPending()
    {
        var stages = RunStageBuilder.Build(Run(
            Step(1, "Mixed", TaskExecutionStatus.Cancelled, skipped: "c"),
            Step(2, "Mixed", TaskExecutionStatus.Pending)));

        var stage = Assert.Single(stages);
        Assert.Equal(TaskExecutionStatus.Pending, stage.Status);
        Assert.Null(stage.SkippedCondition);
    }

    [Fact]
    public void AFailedStepStillDominatesTheStageStatus()
    {
        var stages = RunStageBuilder.Build(Run(
            Step(1, "Build", TaskExecutionStatus.Cancelled, skipped: "c"),
            Step(2, "Build", TaskExecutionStatus.Failed)));

        Assert.Equal(TaskExecutionStatus.Failed, Assert.Single(stages).Status);
    }
}
