// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Tasks;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Tasks;

public class TasksDeepTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public TasksDeepTests() => _handler = BunitTestHelper.RegisterServices(this);

    // The grid + filter + log logic moved into the shared TaskListView component, hosted by both
    // the global /tasks page and the per-server tasks section. Tests target the component directly.
    private IRenderedComponent<TaskListView> RenderView()
    {
        _handler.SetJsonResponse("api/tasks", new PaginatedResult<ServerTaskDto>
        {
            Items =
            [
                new ServerTaskDto { Id = 1, Name = "Build", Status = TaskExecutionStatus.Success },
                new ServerTaskDto { Id = 2, Name = "Deploy", Status = TaskExecutionStatus.Failed }
            ],
            TotalCount = 2
        });
        return Render<TaskListView>();
    }

    // === GetTaskBadge (static) ===

    [Theory]
    [InlineData(TaskExecutionStatus.Success, BadgeStyle.Success)]
    [InlineData(TaskExecutionStatus.Failed, BadgeStyle.Danger)]
    [InlineData(TaskExecutionStatus.Timeout, BadgeStyle.Danger)]
    [InlineData(TaskExecutionStatus.Running, BadgeStyle.Info)]
    [InlineData(TaskExecutionStatus.Cancelled, BadgeStyle.Warning)]
    [InlineData(TaskExecutionStatus.Pending, BadgeStyle.Light)]
    public void GetTaskBadge_ReturnsExpected(TaskExecutionStatus status, BadgeStyle expected)
    {
        Assert.Equal(expected, TaskListView.GetTaskBadge(status));
    }

    // === IsStalledOnOfflineAgent ===

    [Fact]
    public void IsStalledOnOfflineAgent_PendingOnOfflineServer_True()
    {
        var task = new ServerTaskDto { Id = 1, Status = TaskExecutionStatus.Pending, ServerStatus = ServerStatus.Offline };
        Assert.True(TaskListView.IsStalledOnOfflineAgent(task));
    }

    [Fact]
    public void IsStalledOnOfflineAgent_RunningOrOnline_False()
    {
        Assert.False(TaskListView.IsStalledOnOfflineAgent(
            new ServerTaskDto { Status = TaskExecutionStatus.Running, ServerStatus = ServerStatus.Offline }));
        Assert.False(TaskListView.IsStalledOnOfflineAgent(
            new ServerTaskDto { Status = TaskExecutionStatus.Pending, ServerStatus = ServerStatus.Online }));
    }

    // === OnInitialized - status options ===

    [Fact]
    public void OnInitialized_StatusOptions_HasSixItems()
    {
        var cut = RenderView();
        var options = (List<object>)typeof(TaskListView)
            .GetField("_statusOptions", Priv)!.GetValue(cut.Instance)!;
        Assert.Equal(6, options.Count);
    }

    // === Detail navigation ===

    [Fact]
    public void OpenTask_NavigatesToTaskDetail()
    {
        var cut = RenderView();

        cut.InvokeAsync(() => cut.Instance.OpenTask(new ServerTaskDto { Id = 2 }));

        Assert.EndsWith("/tasks/2", Services.GetRequiredService<NavigationManager>().Uri);
    }

    // === ClearFilters ===

    [Fact]
    public async Task ClearFilters_ResetsSearchAndStatusFilter()
    {
        var cut = RenderView();

        typeof(TaskListView).GetField("_search", Priv)!.SetValue(cut.Instance, "deploy");
        typeof(TaskListView).GetField("_statusFilter", Priv)!.SetValue(cut.Instance, (TaskExecutionStatus?)TaskExecutionStatus.Failed);

        var method = typeof(TaskListView).GetMethod("ClearFilters", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        var search = (string?)typeof(TaskListView).GetField("_search", Priv)!.GetValue(cut.Instance);
        var filter = (TaskExecutionStatus?)typeof(TaskListView).GetField("_statusFilter", Priv)!.GetValue(cut.Instance);

        Assert.Null(search);
        Assert.Null(filter);
    }

    // === Render check ===

    [Fact]
    public void Renders_TaskList_WithMarkup()
    {
        var cut = RenderView();
        cut.WaitForState(() => cut.Markup.Contains("Build"), TimeSpan.FromSeconds(2));

        // The stubbed task rows render their names in the grid.
        Assert.Contains("Build", cut.Markup);
        Assert.Contains("Deploy", cut.Markup);
        Assert.Contains("href=\"/tasks/1\"", cut.Markup);
        Assert.DoesNotContain("receipt_long", cut.Markup);
    }
}
