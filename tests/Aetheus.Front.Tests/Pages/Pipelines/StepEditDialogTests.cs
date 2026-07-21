// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages;

public class StepEditDialogTests : BunitContext
{
    public StepEditDialogTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_EmptyForm_WhenNoStep()
    {
        var cut = Render<StepEditDialog>();

        Assert.Contains("StepName", cut.Markup);
        Assert.Contains("ShellCommand", cut.Markup);
        Assert.Contains("TimeoutSeconds", cut.Markup);
    }

    [Fact]
    public void Renders_PrefilledForm_WhenStepProvided()
    {
        var step = new PipelineStepDefinition
        {
            Name = "compile",
            Shell = "dotnet build",
            TimeoutSeconds = 600
        };
        var cut = Render<StepEditDialog>(p => p.Add(x => x.Step, step));

        Assert.Contains("compile", cut.Markup);
        Assert.Contains("dotnet build", cut.Markup);
        Assert.Contains("600", cut.Markup);
    }

    [Fact]
    public void Renders_SaveButton()
    {
        var cut = Render<StepEditDialog>();

        Assert.Contains("Save", cut.Markup);
    }

    [Fact]
    public void Renders_CancelButton()
    {
        var cut = Render<StepEditDialog>();

        Assert.Contains("Cancel", cut.Markup);
    }

    [Fact]
    public void SaveButton_Disabled_WhenNameEmpty()
    {
        var cut = Render<StepEditDialog>();

        var saveBtn = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Save"));
        Assert.NotNull(saveBtn);
        Assert.True(saveBtn.HasAttribute("disabled"));
    }

    [Fact]
    public void SaveButton_Enabled_WhenStepProvided()
    {
        var step = new PipelineStepDefinition
        {
            Name = "test",
            Shell = "dotnet test",
            TimeoutSeconds = 300
        };
        var cut = Render<StepEditDialog>(p => p.Add(x => x.Step, step));

        var saveBtn = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Save"));
        Assert.NotNull(saveBtn);
        Assert.False(saveBtn.HasAttribute("disabled"));
    }
}
