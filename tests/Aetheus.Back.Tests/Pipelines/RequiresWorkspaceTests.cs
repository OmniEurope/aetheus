// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// Guards B1: a trigger-only pipeline (every step <c>type: trigger</c>) needs no build workspace, so
/// <c>PipelineRunService.RequiresWorkspace</c> must return false - which is what suppresses the injected
/// System:Prepare / System:Cleanup stages. Any non-trigger step flips it back to true.
/// </summary>
public class RequiresWorkspaceTests
{
    private static bool Invoke(PipelineYamlDefinition definition) =>
        (bool)typeof(PipelineRunService)
            .GetMethod("RequiresWorkspace", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [definition])!;

    private static PipelineStepDefinition StepOfType(string? type) =>
        new() { Name = "s", Type = type };

    private static PipelineYamlDefinition WithSteps(params PipelineStepDefinition[] steps) =>
        new() { Stages = [new PipelineStageDefinition { Name = "stage", Steps = [.. steps] }] };

    [Fact]
    public void TriggerOnlyPipeline_DoesNotRequireWorkspace()
    {
        var def = WithSteps(StepOfType("trigger"), StepOfType("trigger"));
        Assert.False(Invoke(def));
    }

    [Fact]
    public void TriggerType_IsCaseInsensitive()
    {
        var def = WithSteps(StepOfType("Trigger"), StepOfType("TRIGGER"));
        Assert.False(Invoke(def));
    }

    [Fact]
    public void MixedPipeline_WithOneScriptStep_RequiresWorkspace()
    {
        var def = WithSteps(StepOfType("trigger"), StepOfType(null));
        Assert.True(Invoke(def));
    }

    [Fact]
    public void OrdinaryPipeline_RequiresWorkspace()
    {
        var def = WithSteps(StepOfType(null), StepOfType("deploy"));
        Assert.True(Invoke(def));
    }

    [Fact]
    public void EmptyPipeline_DoesNotRequireWorkspace()
    {
        var def = new PipelineYamlDefinition();
        Assert.False(Invoke(def));
    }
}
