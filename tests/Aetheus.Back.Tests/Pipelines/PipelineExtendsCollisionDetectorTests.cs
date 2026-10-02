// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// S-TECH-K7QX: verifies the base &lt;-&gt; child collision census the template <c>extends</c> merge uses to
/// warn that an appended same-named stage/job/step would become an override under future replace-by-name
/// semantics.
/// </summary>
public sealed class PipelineExtendsCollisionDetectorTests
{
    private static PipelineStageDefinition Stage(string name, params string[] jobNames) => new()
    {
        Name = name,
        Jobs = [.. jobNames.Select(j => new PipelineJobDefinition { Name = j })],
    };

    [Fact]
    public void NoCollision_WhenChildStagesHaveDistinctNames()
    {
        var baseDef = new PipelineYamlDefinition { Stages = [Stage("Build"), Stage("Test")] };
        var child = new PipelineYamlDefinition { Name = "child", Stages = [Stage("Deploy")] };

        Assert.Empty(PipelineExtendsCollisionDetector.FindCollisions(baseDef, child));
    }

    [Fact]
    public void DetectsStageNameCollision_CaseInsensitive()
    {
        var baseDef = new PipelineYamlDefinition { Stages = [Stage("Build")] };
        var child = new PipelineYamlDefinition { Name = "child", Stages = [Stage("build")] };

        var collisions = PipelineExtendsCollisionDetector.FindCollisions(baseDef, child);

        Assert.Contains("stage 'build'", collisions);
    }

    [Fact]
    public void DetectsJobCollision_WithinSameNamedStage()
    {
        var baseDef = new PipelineYamlDefinition { Stages = [Stage("Build", "compile", "lint")] };
        var child = new PipelineYamlDefinition { Name = "child", Stages = [Stage("Build", "compile")] };

        var collisions = PipelineExtendsCollisionDetector.FindCollisions(baseDef, child);

        Assert.Contains("stage 'Build'", collisions);
        Assert.Contains("stage 'Build' > job 'compile'", collisions);
    }

    [Fact]
    public void DetectsStepCollision_WithinSameNamedStage()
    {
        var baseStage = new PipelineStageDefinition
        {
            Name = "Build",
            Steps = [new PipelineStepDefinition { Name = "restore" }],
        };
        var childStage = new PipelineStageDefinition
        {
            Name = "Build",
            Steps = [new PipelineStepDefinition { Name = "restore" }],
        };
        var baseDef = new PipelineYamlDefinition { Stages = [baseStage] };
        var child = new PipelineYamlDefinition { Name = "child", Stages = [childStage] };

        var collisions = PipelineExtendsCollisionDetector.FindCollisions(baseDef, child);

        Assert.Contains("stage 'Build' > step 'restore'", collisions);
    }

    [Fact]
    public void IgnoresEmptyStageNames()
    {
        var baseDef = new PipelineYamlDefinition { Stages = [Stage(string.Empty)] };
        var child = new PipelineYamlDefinition { Name = "child", Stages = [Stage(string.Empty)] };

        Assert.Empty(PipelineExtendsCollisionDetector.FindCollisions(baseDef, child));
    }
}
