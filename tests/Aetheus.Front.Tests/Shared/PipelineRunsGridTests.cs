// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen.Blazor;

namespace Aetheus.Front.Tests.Shared;

public class PipelineRunsGridTests : BunitContext
{
    public PipelineRunsGridTests() => BunitTestHelper.RegisterServices(this);

    [Fact]
    public void GlobalGrid_GroupsRunsByProject_AndRendersCurrentStep()
    {
        var items = new List<PipelineRunTableItem>
        {
            new()
            {
                RunId = 10,
                PipelineId = 1,
                PipelineName = "ci",
                ProjectId = 1,
                ProjectName = "Alpha",
                Status = PipelineStatus.Running,
                CurrentStep = "Build"
            },
            new()
            {
                RunId = 11,
                PipelineId = 2,
                PipelineName = "qa",
                ProjectId = 2,
                ProjectName = "Beta",
                Status = PipelineStatus.Pending,
                CurrentStep = "Tests"
            }
        };
        var cut = Render<PipelineRunsGrid>(parameters => parameters
            .Add(component => component.Items, items)
            .Add(component => component.GroupByProject, true)
            .Add(component => component.Virtualize, true));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Alpha", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Beta", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Build", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Tests", cut.Markup, StringComparison.Ordinal);
            var dataGrid = cut.FindComponent<RadzenDataGrid<PipelineRunTableItem>>().Instance;
            Assert.True(dataGrid.AllowVirtualization);
            Assert.False(dataGrid.AllowPaging);
            Assert.False(dataGrid.AllowGrouping);
        });
    }

    [Fact]
    public void RowClick_NavigatesToProjectScopedRun()
    {
        var item = new PipelineRunTableItem { RunId = 42, PipelineId = 7, PipelineName = "ci", ProjectId = 3 };
        var cut = Render<PipelineRunsGrid>(parameters => parameters.Add(component => component.Items, new List<PipelineRunTableItem> { item }));

        var args = (Radzen.DataGridRowMouseEventArgs<PipelineRunTableItem>)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(Radzen.DataGridRowMouseEventArgs<PipelineRunTableItem>));
        typeof(Radzen.DataGridRowMouseEventArgs<PipelineRunTableItem>)
            .GetField("<Data>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(args, item);
        typeof(PipelineRunsGrid).GetMethod("OnRowClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(cut.Instance, [args]);

        Assert.EndsWith("/pipelines/runs/42?projectId=3", Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void FromRun_ProjectsServerAndCurrentStep()
    {
        var run = new PipelineRunDto
        {
            Id = 9,
            PipelineId = 4,
            PipelineName = "deploy",
            ProjectId = 2,
            ProjectName = "Prod",
            Status = PipelineStatus.Running,
            Steps = [new PipelineStepRunDto { StepName = "Build", Status = TaskExecutionStatus.Running, ServerId = 8, ServerName = "web-01", ServerOs = "Linux" }]
        };

        var item = PipelineRunTableItem.FromRun(run);

        Assert.Equal(9, item.RunId);
        Assert.Equal("web-01", item.ServerName);
        Assert.Equal("Linux", item.ServerOs);
        Assert.Contains("Build", item.CurrentStep);
    }
}
