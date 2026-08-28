// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Shared.DTOs;

namespace Aetheus.Front.Tests.Pages.Pipelines;

/// <summary>
/// Appending a template into an existing pipeline must not silently overwrite a stage the user already
/// has, and must not leave a dependency pointing at a stage that no longer carries that name after the
/// collision was resolved - a run whose dependency names do not resolve never starts.
/// </summary>
public class PipelineStageTemplateMergerTests
{
    private static PipelineStageDefinition Stage(string name, params string[] dependsOn) =>
        new() { Name = name, DependsOn = [.. dependsOn] };

    [Fact]
    public void AppendingToAnEmptyPipeline_KeepsTemplateNamesAndOrder()
    {
        var result = PipelineStageTemplateMerger.Append([], [Stage("build"), Stage("test", "build")]);

        Assert.Equal(["build", "test"], result.Select(s => s.Name));
        Assert.Equal(["build"], result[1].DependsOn);
    }

    [Fact]
    public void ExistingStagesAreKept_AndTheTemplateIsAppendedAfterThem()
    {
        var existing = new[] { Stage("checkout") };

        var result = PipelineStageTemplateMerger.Append(existing, [Stage("build")]);

        Assert.Equal(["checkout", "build"], result.Select(s => s.Name));
    }

    [Fact]
    public void CollidingName_IsSuffixed_AndTheOriginalStageIsUntouched()
    {
        var existing = new[] { Stage("build") };

        var result = PipelineStageTemplateMerger.Append(existing, [Stage("build")]);

        Assert.Equal(["build", "build-2"], result.Select(s => s.Name));
    }

    [Fact]
    public void RepeatedCollisions_KeepIncrementingTheSuffix()
    {
        var existing = new[] { Stage("build"), Stage("build-2") };

        var result = PipelineStageTemplateMerger.Append(existing, [Stage("build")]);

        Assert.Equal("build-3", result[^1].Name);
    }

    /// <summary>
    /// Pins a latent defect found while writing these tests. The unique-name map is keyed by the
    /// template's ORIGINAL stage name, so a template carrying two stages called "deploy" writes that
    /// key twice and the second write wins: both appended stages come out named "deploy-2", i.e. a
    /// pipeline with a duplicate stage name.
    ///
    /// It is only reachable if a template ships duplicate stage names, which the YAML validator is
    /// expected to reject upstream - so this records the behaviour rather than asserting it is right.
    /// If the merger is ever fixed, this test should fail and be replaced by the assertion above it.
    /// </summary>
    [Fact]
    public void TwoTemplateStagesWithTheSameName_CollapseOntoOneResolvedName()
    {
        var result = PipelineStageTemplateMerger.Append([], [Stage("deploy"), Stage("deploy")]);

        Assert.Equal(2, result.Count);
        Assert.Equal("deploy-2", result[0].Name);
        Assert.Equal("deploy-2", result[1].Name);
    }

    [Fact]
    public void InternalDependency_IsRewrittenToTheRenamedStage()
    {
        // The template's "test" depends on its own "build". Since "build" was renamed to avoid the
        // user's stage, the dependency has to follow - otherwise it silently points at the USER's
        // build stage and the run executes in the wrong order.
        var existing = new[] { Stage("build") };

        var result = PipelineStageTemplateMerger.Append(existing, [Stage("build"), Stage("test", "build")]);

        var appendedTest = result.Single(s => s.Name == "test");
        Assert.Equal(["build-2"], appendedTest.DependsOn);
    }

    [Fact]
    public void DependencyOnAnExistingStage_IsPreserved()
    {
        var existing = new[] { Stage("checkout") };

        var result = PipelineStageTemplateMerger.Append(existing, [Stage("build", "checkout")]);

        Assert.Equal(["checkout"], result.Single(s => s.Name == "build").DependsOn);
    }

    [Fact]
    public void DependencyOnAnUnknownStage_IsDropped_SoTheRunCanStart()
    {
        var result = PipelineStageTemplateMerger.Append([], [Stage("build", "does-not-exist")]);

        Assert.Empty(result.Single(s => s.Name == "build").DependsOn);
    }

    [Fact]
    public void TheSourceListsAreNotMutated()
    {
        var existing = new List<PipelineStageDefinition> { Stage("build") };
        var template = new List<PipelineStageDefinition> { Stage("build") };

        PipelineStageTemplateMerger.Append(existing, template);

        Assert.Single(existing);
        Assert.Equal("build", template[0].Name);
    }
}
