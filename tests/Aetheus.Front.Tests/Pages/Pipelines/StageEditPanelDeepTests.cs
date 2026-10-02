// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Pipelines;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Tests.Pages.Pipelines;

public class StageEditPanelDeepTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    public StageEditPanelDeepTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    private static PipelineStageDefinition MakeStage(string name = "Build") =>
        new()
        {
            Name = name,
            Agent = "linux",
            DependsOn = [],
            Variables = new Dictionary<string, string> { ["ENV"] = "prod" },
            Steps =
            [
                new PipelineStepDefinition { Name = "run-tests", Shell = "dotnet test" }
            ]
        };

    [Fact]
    public void Renders_WithStageData()
    {
        var stage = MakeStage();
        var cut = Render<StageEditPanel>(p =>
            p.Add(x => x.Stage, stage)
             .Add(x => x.AvailableServers, ["linux", "windows"])
             .Add(x => x.OtherStageNames, []));
        // The panel binds the stage data into editable fields.
        Assert.Contains("Build", cut.Markup);
        Assert.Contains("run-tests", cut.Markup);
    }

    [Fact]
    public async Task OnNameChanged_InvokesStageChanged()
    {
        PipelineStageDefinition? received = null;
        var stage = MakeStage();
        var cut = Render<StageEditPanel>(p =>
            p.Add(x => x.Stage, stage)
             .Add(x => x.AvailableServers, [])
             .Add(x => x.OtherStageNames, [])
             .Add(x => x.StageChanged, EventCallback.Factory.Create<PipelineStageDefinition>(this, s => received = s)));

        var method = typeof(StageEditPanel).GetMethod("OnNameChanged", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, ["NewName"])!);
        Assert.NotNull(received);
        Assert.Equal("NewName", received!.Name);
    }

    [Fact]
    public async Task OnAgentChanged_UpdatesAgent()
    {
        PipelineStageDefinition? received = null;
        var stage = MakeStage();
        var cut = Render<StageEditPanel>(p =>
            p.Add(x => x.Stage, stage)
             .Add(x => x.AvailableServers, ["windows"])
             .Add(x => x.OtherStageNames, [])
             .Add(x => x.StageChanged, EventCallback.Factory.Create<PipelineStageDefinition>(this, s => received = s)));

        var method = typeof(StageEditPanel).GetMethod("OnAgentChanged", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, ["windows"])!);
        Assert.NotNull(received);
        Assert.Equal("windows", received!.Agent);
    }

    [Fact]
    public async Task OnAddVariable_AddsVar_WhenKeyNotEmpty()
    {
        PipelineStageDefinition? received = null;
        var stage = MakeStage();
        var cut = Render<StageEditPanel>(p =>
            p.Add(x => x.Stage, stage)
             .Add(x => x.AvailableServers, [])
             .Add(x => x.OtherStageNames, [])
             .Add(x => x.StageChanged, EventCallback.Factory.Create<PipelineStageDefinition>(this, s => received = s)));

        typeof(StageEditPanel).GetField("_newVarKey", Priv)!.SetValue(cut.Instance, "MY_VAR");
        typeof(StageEditPanel).GetField("_newVarValue", Priv)!.SetValue(cut.Instance, "123");

        var method = typeof(StageEditPanel).GetMethod("OnAddVariable", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);
        Assert.NotNull(received);
        Assert.True(received!.Variables.ContainsKey("MY_VAR"));
    }

    [Fact]
    public async Task OnAddVariable_DoesNothing_WhenKeyEmpty()
    {
        var changed = false;
        var stage = MakeStage();
        var cut = Render<StageEditPanel>(p =>
            p.Add(x => x.Stage, stage)
             .Add(x => x.AvailableServers, [])
             .Add(x => x.OtherStageNames, [])
             .Add(x => x.StageChanged, EventCallback.Factory.Create<PipelineStageDefinition>(this, _ => changed = true)));

        var method = typeof(StageEditPanel).GetMethod("OnAddVariable", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);
        Assert.False(changed);
    }

    [Fact]
    public async Task OnRemoveVariable_RemovesKey()
    {
        PipelineStageDefinition? received = null;
        var stage = MakeStage();
        var cut = Render<StageEditPanel>(p =>
            p.Add(x => x.Stage, stage)
             .Add(x => x.AvailableServers, [])
             .Add(x => x.OtherStageNames, [])
             .Add(x => x.StageChanged, EventCallback.Factory.Create<PipelineStageDefinition>(this, s => received = s)));

        var method = typeof(StageEditPanel).GetMethod("OnRemoveVariable", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, ["ENV"])!);
        Assert.NotNull(received);
        Assert.False(received!.Variables.ContainsKey("ENV"));
    }

    [Fact]
    public async Task OnMoveStep_MovesStepDown()
    {
        PipelineStageDefinition? received = null;
        var stage = MakeStage() with
        {
            Steps =
            [
                new PipelineStepDefinition { Name = "step-a", Shell = "echo a" },
                new PipelineStepDefinition { Name = "step-b", Shell = "echo b" }
            ]
        };
        var cut = Render<StageEditPanel>(p =>
            p.Add(x => x.Stage, stage)
             .Add(x => x.AvailableServers, [])
             .Add(x => x.OtherStageNames, [])
             .Add(x => x.StageChanged, EventCallback.Factory.Create<PipelineStageDefinition>(this, s => received = s)));

        var method = typeof(StageEditPanel).GetMethod("OnMoveStep", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [0, 1])!);
        Assert.NotNull(received);
        Assert.Equal("step-b", received!.Steps[0].Name);
    }

    [Fact]
    public async Task OnRemoveStep_RemovesStep()
    {
        PipelineStageDefinition? received = null;
        var stage = MakeStage() with
        {
            Steps =
            [
                new PipelineStepDefinition { Name = "step-a", Shell = "echo a" },
                new PipelineStepDefinition { Name = "step-b", Shell = "echo b" }
            ]
        };
        var cut = Render<StageEditPanel>(p =>
            p.Add(x => x.Stage, stage)
             .Add(x => x.AvailableServers, [])
             .Add(x => x.OtherStageNames, [])
             .Add(x => x.StageChanged, EventCallback.Factory.Create<PipelineStageDefinition>(this, s => received = s)));

        var method = typeof(StageEditPanel).GetMethod("OnRemoveStep", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [0])!);
        Assert.NotNull(received);
        Assert.Single(received!.Steps);
    }
}
