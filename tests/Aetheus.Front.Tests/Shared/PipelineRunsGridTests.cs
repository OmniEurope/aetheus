// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Radzen.Blazor;

namespace Aetheus.Front.Tests.Shared;

public class PipelineRunsGridTests : BunitContext
{
    public PipelineRunsGridTests() => BunitTestHelper.RegisterServices(this);

    [Fact]
    public void LoadingProgressBar_HasAccessibleName()
    {
        var cut = Render<PipelineRunsGrid>(parameters => parameters
            .Add(component => component.IsLoading, true));

        var progressBar = cut.Find("[role='progressbar']");
        Assert.False(string.IsNullOrWhiteSpace(progressBar.GetAttribute("aria-label")), cut.Markup);
    }

    [Fact]
    public void GlobalGrid_GroupsRunsByProject_AndRendersCurrentStep()
    {
        var items = new List<PipelineRunDto>
        {
            new()
            {
                Id = 10,
                PipelineId = 1,
                PipelineName = "ci",
                ProjectId = 1,
                ProjectName = "Alpha",
                Status = PipelineStatus.Running,
                Steps = [new PipelineStepRunDto { StepName = "Build", StageName = "Compile", Status = TaskExecutionStatus.Running }]
            },
            new()
            {
                Id = 11,
                PipelineId = 2,
                PipelineName = "qa",
                ProjectId = 2,
                ProjectName = "Beta",
                Status = PipelineStatus.Pending,
                Steps = [new PipelineStepRunDto { StepName = "Tests", StageName = "Verify", Status = TaskExecutionStatus.Running }]
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
        var run = new PipelineRunDto { Id = 42, PipelineId = 7, PipelineName = "ci", ProjectId = 3 };
        var item = PipelineRunTableItem.FromRun(run);
        var cut = Render<PipelineRunsGrid>(parameters => parameters.Add(component => component.Items, new List<PipelineRunDto> { run }));

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
    public void Grid_RendersDurationAndServer()
    {
        var startedAt = new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Local);
        var item = new PipelineRunDto
        {
            Id = 9,
            PipelineId = 4,
            PipelineName = "deploy",
            Status = PipelineStatus.Success,
            StartedAt = startedAt,
            CompletedAt = startedAt.AddMinutes(2).AddSeconds(5),
            Steps = [new PipelineStepRunDto { ServerId = 1, ServerName = "web-01" }]
        };

        var cut = Render<PipelineRunsGrid>(parameters => parameters
            .Add(component => component.Items, new List<PipelineRunDto> { item }));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Duration", cut.Markup);
            Assert.Contains("DurationMinutesSecondsFormat", cut.Markup);
            // The server used to be deliberately absent here. It is now a column of its own, so the
            // run table answers "where did this run" without opening the run.
            Assert.Contains("web-01", cut.Markup);
        });
    }

    [Fact]
    public void RunningDuration_AdvancesEverySecond_WithoutRefreshingTheGrid()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 12, 0, 5, TimeSpan.Zero));
        var localizer = Substitute.For<IStringLocalizer<AppStrings>>();
        localizer["DurationSecondsFormat"].Returns(new LocalizedString("DurationSecondsFormat", "{0}s"));
        Services.AddSingleton<TimeProvider>(time);
        Services.AddSingleton(localizer);
        var run = new PipelineRunDto
        {
            Id = 21,
            PipelineId = 4,
            PipelineName = "deploy",
            Status = PipelineStatus.Running,
            StartedAt = time.GetLocalNow().LocalDateTime.AddSeconds(-5)
        };

        var cut = Render<PipelineRunsGrid>(parameters => parameters
            .Add(component => component.Items, new List<PipelineRunDto> { run }));

        cut.WaitForAssertion(() => Assert.Equal("5s", cut.Find(".pipeline-run-duration").TextContent.Trim()));

        time.Advance(TimeSpan.FromSeconds(2));

        cut.WaitForAssertion(() => Assert.Equal("7s", cut.Find(".pipeline-run-duration").TextContent.Trim()));
    }

    [Fact]
    public void CompletedDuration_RemainsFixed_WhenTimeAdvances()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 12, 0, 5, TimeSpan.Zero));
        var localizer = Substitute.For<IStringLocalizer<AppStrings>>();
        localizer["DurationSecondsFormat"].Returns(new LocalizedString("DurationSecondsFormat", "{0}s"));
        Services.AddSingleton<TimeProvider>(time);
        Services.AddSingleton(localizer);
        var startedAt = time.GetLocalNow().LocalDateTime.AddSeconds(-10);
        var run = PipelineRunTableItem.FromRun(new PipelineRunDto
        {
            Id = 22,
            PipelineId = 4,
            PipelineName = "deploy",
            Status = PipelineStatus.Success,
            StartedAt = startedAt,
            CompletedAt = startedAt.AddSeconds(6)
        });

        var cut = Render<PipelineRunTableLiveDuration>(parameters => parameters
            .Add(component => component.Run, run));

        Assert.Equal("6s", cut.Markup.Trim());

        time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal("6s", cut.Markup.Trim());
    }

    [Fact]
    public void CompactGrid_ShowsRunIdFirst_AndDurationAfterStarted_WhenRequested()
    {
        var startedAt = new DateTime(2026, 8, 5, 10, 0, 0, DateTimeKind.Local);
        var run = new PipelineRunDto
        {
            Id = 15,
            PipelineId = 4,
            PipelineName = "deploy",
            Status = PipelineStatus.Success,
            StartedAt = startedAt,
            CompletedAt = startedAt.AddMinutes(3)
        };

        var cut = Render<PipelineRunsGrid>(parameters => parameters
            .Add(component => component.Items, new List<PipelineRunDto> { run })
            .Add(component => component.Compact, true)
            .Add(component => component.ShowDurationInCompact, true));

        cut.WaitForAssertion(() =>
        {
            // Column contract, in the order the run tables must keep: the id first, then the action
            // buttons, and the current step last so the wide, live-updating column never pushes the
            // stable identification columns off screen.
            var columns = cut.FindComponent<RadzenDataGrid<PipelineRunTableItem>>().Instance.ColumnsCollection.ToList();
            Assert.Equal("RunId", columns[0].Property);
            Assert.Equal("Actions", columns[1].Title);
            Assert.Equal("PipelineName", columns[2].Property);
            Assert.Equal("CurrentStep", columns[^1].Title);
            var titles = columns.Select(column => column.Title).ToList();
            Assert.Equal(
                ["Status", "Started", "Completed", "Duration", "Branch", "Commit", "Server"],
                titles.Where(title => title is "Status" or "Started" or "Completed" or "Duration" or "Branch" or "Commit" or "Server").ToList());
            Assert.Contains("DurationMinutesSecondsFormat", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MaxGroups_LimitsTheRenderedDataToTheTenMostRecentGroups()
    {
        var startedAt = new DateTime(2026, 8, 5, 10, 0, 0, DateTimeKind.Local);
        var runs = Enumerable.Range(1, 11)
            .Select(id => new PipelineRunDto
            {
                Id = id,
                PipelineId = id,
                PipelineName = $"pipeline-{id}",
                Status = PipelineStatus.Success,
                StartedAt = startedAt.AddMinutes(id)
            })
            .ToList();

        var cut = Render<PipelineRunsGrid>(parameters => parameters
            .Add(component => component.Items, runs)
            .Add(component => component.MaxGroups, 10));

        cut.WaitForAssertion(() =>
        {
            var renderedRuns = cut.FindComponent<RadzenDataGrid<PipelineRunTableItem>>().Instance.Data!.ToList();
            Assert.Equal(10, renderedRuns.Count);
            Assert.Equal(Enumerable.Range(2, 10).Reverse(), renderedRuns.Select(run => run.RunId));
        });
    }

    [Fact]
    public void FromRun_ProjectsTimingAndCurrentStep()
    {
        var startedAt = new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Local);
        var run = new PipelineRunDto
        {
            Id = 9,
            PipelineId = 4,
            PipelineName = "deploy",
            ProjectId = 2,
            ProjectName = "Prod",
            Status = PipelineStatus.Running,
            StartedAt = startedAt,
            CompletedAt = startedAt.AddSeconds(42),
            Steps = [new PipelineStepRunDto { StepName = "Build", Status = TaskExecutionStatus.Running, ServerId = 8, ServerName = "web-01", ServerOs = "Linux" }]
        };

        var item = PipelineRunTableItem.FromRun(run);

        Assert.Equal(9, item.RunId);
        Assert.Equal("web-01", item.ServerName);
        Assert.Equal("Linux", item.ServerOs);
        Assert.Equal(startedAt, item.StartedAt);
        Assert.Equal(startedAt.AddSeconds(42), item.CompletedAt);
        Assert.Contains("Build", item.CurrentStep);
    }

    [Fact]
    public void LinkedRuns_AreGroupedUnderTheirRoot_AndAllGroupsStartCollapsed()
    {
        var oldStartedAt = new DateTime(2026, 7, 30, 10, 0, 0, DateTimeKind.Local);
        var latestStartedAt = oldStartedAt.AddHours(2);
        var runs = new List<PipelineRunDto>
        {
            new()
            {
                Id = 100,
                PipelineId = 1,
                PipelineName = "old-parent",
                Status = PipelineStatus.Success,
                StartedAt = oldStartedAt,
                Steps = [new PipelineStepRunDto { Id = 1, TriggeredRunId = 101 }]
            },
            new()
            {
                Id = 101,
                PipelineId = 2,
                PipelineName = "old-child",
                Status = PipelineStatus.Success,
                StartedAt = oldStartedAt.AddMinutes(1)
            },
            new()
            {
                Id = 200,
                PipelineId = 3,
                PipelineName = "latest-parent",
                Status = PipelineStatus.Running,
                StartedAt = latestStartedAt,
                Steps = [new PipelineStepRunDto { Id = 2, TriggeredRunId = 201 }]
            },
            new()
            {
                Id = 201,
                PipelineId = 4,
                PipelineName = "latest-child",
                Status = PipelineStatus.Running,
                StartedAt = latestStartedAt.AddMinutes(1)
            }
        };

        var cut = Render<PipelineRunsGrid>(parameters => parameters
            .Add(component => component.Items, runs));

        cut.WaitForAssertion(() =>
        {
            var grid = cut.FindComponent<RadzenDataGrid<PipelineRunTableItem>>().Instance;
            Assert.Equal(2, (grid.Data ?? []).Count());
            Assert.DoesNotContain("latest-child", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("old-child", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("ExpandLinkedPipelineRuns", cut.Markup, StringComparison.Ordinal);
        });

        cut.Find("button[aria-label='ExpandLinkedPipelineRuns']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("latest-child", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("old-child", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ExpandedLinkedRuns_RemainOpen_WhenItemsRefresh()
    {
        static List<PipelineRunDto> CreateRuns() =>
        [
            new PipelineRunDto
            {
                Id = 100,
                PipelineId = 1,
                PipelineName = "parent",
                Status = PipelineStatus.Running,
                StartedAt = new DateTime(2026, 8, 5, 10, 0, 0, DateTimeKind.Local),
                Steps = [new PipelineStepRunDto { Id = 1, TriggeredRunId = 101 }]
            },
            new PipelineRunDto
            {
                Id = 101,
                PipelineId = 2,
                PipelineName = "child",
                Status = PipelineStatus.Success,
                StartedAt = new DateTime(2026, 8, 5, 10, 1, 0, DateTimeKind.Local)
            }
        ];

        var cut = Render<PipelineRunsGrid>(parameters => parameters
            .Add(component => component.Items, CreateRuns())
            .Add(component => component.Compact, true)
            .Add(component => component.ShowDurationInCompact, true));

        cut.Find("button[aria-label='ExpandLinkedPipelineRuns']").Click();
        cut.WaitForAssertion(() => Assert.Contains("child", cut.Markup, StringComparison.Ordinal));

        cut.Render(parameters => parameters
            .Add(component => component.Items, CreateRuns())
            .Add(component => component.Compact, true)
            .Add(component => component.ShowDurationInCompact, true));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("child", cut.Markup, StringComparison.Ordinal);
            Assert.Single(cut.FindAll(".pipeline-linked-runs"));
            Assert.Contains("pipeline-run-tree-with-duration", cut.Find(".pipeline-run-tree").ClassList);
            Assert.Equal(
                [
                    "pipeline-run-tree-leaf",
                    "pipeline-run-tree-id",
                    "pipeline-name-link pipeline-run-tree-name",
                    "pipeline-run-tree-status",
                    "pipeline-run-tree-started",
                    "pipeline-run-tree-duration"
                ],
                cut.Find(".pipeline-run-tree-row").Children.Select(element => element.ClassName));
        });
    }

    [Fact]
    public void GroupRuns_PreservesTheDirectParentChildHierarchy()
    {
        var runs = new List<PipelineRunDto>
        {
            new()
            {
                Id = 10,
                PipelineId = 1,
                PipelineName = "parent",
                Steps = [new PipelineStepRunDto { Id = 1, TriggeredRunId = 11 }]
            },
            new()
            {
                Id = 11,
                PipelineId = 2,
                PipelineName = "child",
                Steps = [new PipelineStepRunDto { Id = 2, TriggeredRunId = 12 }]
            },
            new() { Id = 12, PipelineId = 3, PipelineName = "grandchild" }
        };

        var group = Assert.Single(PipelineRunTableItem.GroupRuns(runs));

        Assert.Equal(10, group.RunId);
        var child = Assert.Single(group.LinkedRuns);
        Assert.Equal(11, child.RunId);
        Assert.Equal(12, Assert.Single(child.LinkedRuns).RunId);
    }

    [Fact]
    public void NestedRuns_AppearOnlyAfterTheirDirectParentIsExpanded()
    {
        var runs = new List<PipelineRunDto>
        {
            new()
            {
                Id = 10,
                PipelineId = 1,
                PipelineName = "parent",
                StartedAt = new DateTime(2026, 7, 30, 12, 0, 0),
                Steps = [new PipelineStepRunDto { Id = 1, TriggeredRunId = 11 }]
            },
            new()
            {
                Id = 11,
                PipelineId = 2,
                PipelineName = "direct-child",
                StartedAt = new DateTime(2026, 7, 30, 12, 1, 0),
                Steps = [new PipelineStepRunDto { Id = 2, TriggeredRunId = 12 }]
            },
            new()
            {
                Id = 12,
                PipelineId = 3,
                PipelineName = "grandchild",
                StartedAt = new DateTime(2026, 7, 30, 12, 2, 0)
            }
        };

        var cut = Render<PipelineRunsGrid>(parameters => parameters
            .Add(component => component.Items, runs));

        cut.WaitForAssertion(() => Assert.DoesNotContain("direct-child", cut.Markup, StringComparison.Ordinal));

        cut.Find("button[aria-label='ExpandLinkedPipelineRuns']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("direct-child", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("grandchild", cut.Markup, StringComparison.Ordinal);
        });

        cut.Find(".pipeline-run-tree-toggle").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("grandchild", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("CollapseLinkedPipelineRuns", cut.Markup, StringComparison.Ordinal);
        });
    }
}
