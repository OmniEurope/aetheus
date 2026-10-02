// SPDX-License-Identifier: EUPL-1.2
using Bunit;

namespace Aetheus.Front.Tests.Pages;

public class PipelineSelectDialogTests : BunitContext
{
    public PipelineSelectDialogTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_Dropdown()
    {
        var pipelines = new List<PipelineDto>
        {
            new() { Id = 1, Name = "CI Build" },
            new() { Id = 2, Name = "Deploy Prod" }
        };

        var cut = Render<PipelineSelectDialog>(p => p.Add(x => x.Pipelines, pipelines));
        Assert.Contains("SelectPipeline", cut.Markup);
        // The dropdown must render an item per pipeline, labelled by its Name.
        var items = cut.FindAll("select option:not([value=''])");
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.TextContent.Contains("CI Build"));
        Assert.Contains(items, i => i.TextContent.Contains("Deploy Prod"));
    }

    [Fact]
    public void Renders_ConfirmButton_Disabled_WhenNoneSelected()
    {
        var pipelines = new List<PipelineDto> { new() { Id = 1, Name = "CI" } };
        var cut = Render<PipelineSelectDialog>(p => p.Add(x => x.Pipelines, pipelines));

        var buttons = cut.FindAll("button");
        var confirmBtn = buttons.FirstOrDefault(b => b.TextContent.Contains("Confirm"));
        Assert.NotNull(confirmBtn);
        Assert.True(confirmBtn.HasAttribute("disabled"));
    }

    [Fact]
    public void Renders_CancelButton()
    {
        var cut = Render<PipelineSelectDialog>(p => p.Add(x => x.Pipelines, new List<PipelineDto>()));
        var buttons = cut.FindAll("button");
        var cancelBtn = buttons.FirstOrDefault(b => b.TextContent.Contains("GoBack"));
        Assert.NotNull(cancelBtn);
    }

    [Fact]
    public void Renders_EmptyPipelines()
    {
        var cut = Render<PipelineSelectDialog>(p => p.Add(x => x.Pipelines, new List<PipelineDto>()));
        // Even with no pipelines the dropdown placeholder and action buttons render.
        Assert.Contains("SelectPipeline", cut.Markup);
        Assert.Contains("Confirm", cut.Markup);
    }
}
