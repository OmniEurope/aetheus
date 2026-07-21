// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen.Blazor;

namespace Aetheus.Front.Tests.Shared;

public class PipelineDependencyGridTests : BunitContext
{
    public PipelineDependencyGridTests() => BunitTestHelper.RegisterServices(this);

    [Fact]
    public void ParentRow_ExpandsChildrenInProvidedExecutionOrder()
    {
        var item = new PipelineDependencyDto
        {
            Id = 1,
            Name = "release",
            References =
            [
                new PipelineDependencyReferenceDto(2, "ci"),
                new PipelineDependencyReferenceDto(3, "qa"),
                new PipelineDependencyReferenceDto(4, "deploy")
            ]
        };
        var cut = RenderGrid(item, showsChildren: true);

        cut.Find(".rz-row-toggler").ParentElement!.Click();

        cut.WaitForAssertion(() =>
        {
            var markup = cut.Markup;
            Assert.True(markup.IndexOf(">ci<", StringComparison.Ordinal) < markup.IndexOf(">qa<", StringComparison.Ordinal));
            Assert.True(markup.IndexOf(">qa<", StringComparison.Ordinal) < markup.IndexOf(">deploy<", StringComparison.Ordinal));
            Assert.Contains("pipeline-relation-list-execution", markup);
            Assert.Equal(3, cut.FindAll(".pipeline-relation-marker").Count);
            Assert.All(cut.FindAll(".pipeline-relation-card"), card => Assert.Contains("pipeline-relation-name", card.InnerHtml));
        });
    }

    [Fact]
    public void LeafRow_ExpandsParentsAlphabetically_AndEditTargetsYamlTab()
    {
        var item = new PipelineDependencyDto
        {
            Id = 4,
            Name = "deploy",
            Parents =
            [
                new PipelineDependencyReferenceDto(2, "zeta"),
                new PipelineDependencyReferenceDto(1, "alpha")
            ]
        };
        var cut = RenderGrid(item, showsChildren: false);
        cut.Find(".rz-row-toggler").ParentElement!.Click();

        cut.WaitForAssertion(() => Assert.True(
            cut.Markup.IndexOf(">alpha<", StringComparison.Ordinal)
            < cut.Markup.IndexOf(">zeta<", StringComparison.Ordinal)));
        Assert.Contains("pipeline-relation-list-parents", cut.Markup);

        cut.FindAll("button").Single(button => button.TextContent.Contains("Edit", StringComparison.Ordinal)).Click();
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        Assert.EndsWith("/pipelines/4?tab=edit", nav.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void RunButton_InvokesCallbackWithPipelineId()
    {
        (int Id, RadzenSplitButtonItem? Item)? invocation = null;
        var item = new PipelineDependencyDto { Id = 17, Name = "release" };
        Func<int, RadzenSplitButtonItem?, Task> run = (id, selected) => { invocation = (id, selected); return Task.CompletedTask; };
        var cut = Render<PipelineDependencyGrid>(parameters => parameters
            .Add(component => component.Items, new List<PipelineDependencyDto> { item })
            .Add(component => component.PipelineHref, id => $"/pipelines/{id}")
            .Add(component => component.RunPipeline, run)
            .Add(component => component.CanWrite, true));

        cut.FindAll("button").Single(button => button.TextContent.Contains("Run", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(invocation);
            Assert.Equal(17, invocation.Value.Id);
            Assert.Null(invocation.Value.Item);
        });
    }

    [Fact]
    public void GlobalGrid_GroupsRowsByProject()
    {
        var items = new List<PipelineDependencyDto>
        {
            new() { Id = 1, Name = "ci", ProjectName = "Alpha" },
            new() { Id = 2, Name = "qa", ProjectName = "Beta" }
        };
        var cut = Render<PipelineDependencyGrid>(parameters => parameters
            .Add(component => component.Items, items)
            .Add(component => component.PipelineHref, id => $"/pipelines/{id}")
            .Add(component => component.RunPipeline, (_, _) => Task.CompletedTask)
            .Add(component => component.GroupByProject, true)
            .Add(component => component.Virtualize, true));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Alpha", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Beta", cut.Markup, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll(".rz-row-toggler"));
            var dataGrid = cut.FindComponent<RadzenDataGrid<PipelineDependencyDto>>().Instance;
            Assert.True(dataGrid.AllowVirtualization);
            Assert.False(dataGrid.AllowPaging);
            Assert.False(dataGrid.AllowGrouping);
        });
    }

    [Fact]
    public void Rows_RenderFiveRecentRunStatusDots()
    {
        var item = new PipelineDependencyDto
        {
            Id = 5,
            Name = "ci",
            ProjectId = 8,
            RecentRuns =
            [
                new PipelineRunSummaryDto { Id = 15, Status = Aetheus.Shared.Enums.PipelineStatus.Running },
                new PipelineRunSummaryDto { Id = 14, Status = Aetheus.Shared.Enums.PipelineStatus.Failed },
                new PipelineRunSummaryDto { Id = 13, Status = Aetheus.Shared.Enums.PipelineStatus.Success },
                new PipelineRunSummaryDto { Id = 12, Status = Aetheus.Shared.Enums.PipelineStatus.Cancelled },
                new PipelineRunSummaryDto { Id = 11, Status = Aetheus.Shared.Enums.PipelineStatus.Pending }
            ]
        };

        var cut = RenderGrid(item, showsChildren: false);

        var dots = cut.FindAll(".pipeline-run-history .pipeline-run-dot");
        Assert.Equal(5, dots.Count);
        Assert.Equal("/pipelines/runs/11?projectId=8", dots[0].GetAttribute("href"));
        Assert.Equal("/pipelines/runs/15?projectId=8", dots[^1].GetAttribute("href"));
        Assert.Contains("pipeline-run-dot-running", dots[^1].ClassList);
    }

    private IRenderedComponent<PipelineDependencyGrid> RenderGrid(PipelineDependencyDto item, bool showsChildren) =>
        Render<PipelineDependencyGrid>(parameters => parameters
            .Add(component => component.Items, new List<PipelineDependencyDto> { item })
            .Add(component => component.PipelineHref, id => $"/pipelines/{id}")
            .Add(component => component.RunPipeline, (_, _) => Task.CompletedTask)
            .Add(component => component.ShowsChildren, showsChildren)
            .Add(component => component.CanWrite, true));
}
