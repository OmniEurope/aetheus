// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Tests.Pages;

public class StageEditPanelTests : BunitContext
{
    public StageEditPanelTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    private static PipelineStageDefinition CreateStage() => new()
    {
        Name = "build",
        Agent = "server-1",
        Variables = new Dictionary<string, string> { ["ENV"] = "dev" },
        Steps =
        [
            new PipelineStepDefinition { Name = "compile", Shell = "dotnet build", TimeoutSeconds = 300 },
            new PipelineStepDefinition { Name = "test", Shell = "dotnet test", TimeoutSeconds = 600 }
        ]
    };

    [Fact]
    public void Renders_StageName()
    {
        var stage = CreateStage();
        var cut = Render<StageEditPanel>(p => p
            .Add(x => x.Stage, stage)
            .Add(x => x.AvailableServers, new List<string> { "server-1", "server-2" })
            .Add(x => x.OtherStageNames, new List<string> { "deploy" }));

        Assert.Contains("build", cut.Markup);
        Assert.Contains("EditStage", cut.Markup);
    }

    [Fact]
    public void Renders_Steps()
    {
        var stage = CreateStage();
        var cut = Render<StageEditPanel>(p => p
            .Add(x => x.Stage, stage)
            .Add(x => x.AvailableServers, new List<string>())
            .Add(x => x.OtherStageNames, new List<string>()));

        Assert.Contains("compile", cut.Markup);
        Assert.Contains("test", cut.Markup);
    }

    [Fact]
    public void Renders_AddStepButton()
    {
        var stage = CreateStage();
        var cut = Render<StageEditPanel>(p => p
            .Add(x => x.Stage, stage)
            .Add(x => x.AvailableServers, new List<string>())
            .Add(x => x.OtherStageNames, new List<string>()));

        Assert.Contains("AddStep", cut.Markup);
    }

    [Fact]
    public void Renders_DeleteStageButton()
    {
        var stage = CreateStage();
        var cut = Render<StageEditPanel>(p => p
            .Add(x => x.Stage, stage)
            .Add(x => x.AvailableServers, new List<string>())
            .Add(x => x.OtherStageNames, new List<string>()));

        Assert.Contains("DeleteStage", cut.Markup);
    }

    [Fact]
    public void Renders_CloseButton()
    {
        var stage = CreateStage();
        var cut = Render<StageEditPanel>(p => p
            .Add(x => x.Stage, stage)
            .Add(x => x.AvailableServers, new List<string>())
            .Add(x => x.OtherStageNames, new List<string>()));

        var closeBtn = cut.FindAll("button").FirstOrDefault(b => b.InnerHtml.Contains("close"));
        Assert.NotNull(closeBtn);
    }

    [Fact]
    public async Task CloseButton_InvokesOnClose()
    {
        var closed = false;
        var stage = CreateStage();
        var cut = Render<StageEditPanel>(p => p
            .Add(x => x.Stage, stage)
            .Add(x => x.AvailableServers, new List<string>())
            .Add(x => x.OtherStageNames, new List<string>())
            .Add(x => x.OnClose, () => { closed = true; }));

        var closeBtn = cut.FindAll("button").First(b => b.InnerHtml.Contains("close"));
        await cut.InvokeAsync(() => closeBtn.Click());

        Assert.True(closed);
    }

    [Fact]
    public async Task DeleteStage_InvokesCallback()
    {
        var deleted = false;
        var stage = CreateStage();
        var cut = Render<StageEditPanel>(p => p
            .Add(x => x.Stage, stage)
            .Add(x => x.AvailableServers, new List<string>())
            .Add(x => x.OtherStageNames, new List<string>())
            .Add(x => x.OnDeleteStage, () => { deleted = true; }));

        var deleteBtn = cut.FindAll("button").First(b => b.TextContent.Contains("DeleteStage"));
        await cut.InvokeAsync(() => deleteBtn.Click());

        Assert.True(deleted);
    }

    [Fact]
    public void Renders_EmptyStage()
    {
        var stage = new PipelineStageDefinition
        {
            Name = "empty",
            Agent = "",
            Variables = [],
            Steps = []
        };
        var cut = Render<StageEditPanel>(p => p
            .Add(x => x.Stage, stage)
            .Add(x => x.AvailableServers, new List<string>())
            .Add(x => x.OtherStageNames, new List<string>()));

        Assert.Contains("empty", cut.Markup);
        Assert.Contains("AddStep", cut.Markup);
    }

    [Fact]
    public void Renders_StepMoveButtons()
    {
        var stage = CreateStage();
        var cut = Render<StageEditPanel>(p => p
            .Add(x => x.Stage, stage)
            .Add(x => x.AvailableServers, new List<string>())
            .Add(x => x.OtherStageNames, new List<string>()));

        Assert.Contains("arrow_upward", cut.Markup);
        Assert.Contains("arrow_downward", cut.Markup);
    }

    [Fact]
    public void Renders_VariablesFieldset()
    {
        var stage = CreateStage();
        var cut = Render<StageEditPanel>(p => p
            .Add(x => x.Stage, stage)
            .Add(x => x.AvailableServers, new List<string>())
            .Add(x => x.OtherStageNames, new List<string>()));

        Assert.Contains("StageVariables", cut.Markup);
    }
}
