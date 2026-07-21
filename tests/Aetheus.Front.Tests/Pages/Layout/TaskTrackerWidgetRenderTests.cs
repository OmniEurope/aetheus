// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Layout;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Front.Tests.Pages.Layout;

public class TaskTrackerWidgetRenderTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type WidgetType = typeof(TaskTrackerWidget);

    public TaskTrackerWidgetRenderTests()
    {
        BunitTestHelper.RegisterServices(this, authenticated: false);
        Services.AddScoped(sp => new TaskTrackerService(
            sp.GetRequiredService<ApiClient>(),
            sp.GetRequiredService<AuthStateProvider>(),
            sp.GetRequiredService<HubConnectionFactory>(),
            NullLogger<TaskTrackerService>.Instance));
    }

    [Fact]
    public void Renders_WithZeroCount_ShowsTaskAltIcon()
    {
        var cut = Render<TaskTrackerWidget>();
        // icon should be task_alt when count == 0
        Assert.Contains("task_alt", cut.Markup);
    }

    [Fact]
    public void Renders_WithNonZeroCount_ShowsBadge()
    {
        var cut = Render<TaskTrackerWidget>();
        // Inject tasks into tracker to set count > 0
        var tracker = Services.GetRequiredService<TaskTrackerService>();
        var byId = typeof(TaskTrackerService)
            .GetField("_byId", Priv)!.GetValue(tracker) as Dictionary<int, ServerTaskDto>;
        byId![1] = new ServerTaskDto { Id = 1, ServerId = 10, ServerName = "srv1", Name = "Deploy", Status = TaskExecutionStatus.Running, CreatedAt = DateTime.UtcNow, Command = "cmd", TimeoutSeconds = 60 };

        var onChanged = typeof(TaskTrackerService).GetEvent("OnChanged")!;
        var tracker_changed = WidgetType.GetMethod("OnTrackerChanged", Priv)!;
        tracker_changed.Invoke(cut.Instance, []);

        cut.Render();
        Assert.Contains("task-tracker-badge", cut.Markup);
    }

    [Fact]
    public void TogglePopover_OpensAndClosesPopover()
    {
        var cut = Render<TaskTrackerWidget>();
        var toggle = WidgetType.GetMethod("TogglePopover", Priv)!;

        // Open
        toggle.Invoke(cut.Instance, null);
        cut.Render();
        Assert.Single(cut.FindAll("#task-tracker-popover"));

        // Close
        toggle.Invoke(cut.Instance, null);
        cut.Render();
        Assert.Empty(cut.FindAll("#task-tracker-popover"));
    }

    [Fact]
    public void PopoverOpen_ShowsEmptyState_WhenNoTasks()
    {
        var cut = Render<TaskTrackerWidget>();
        var toggle = WidgetType.GetMethod("TogglePopover", Priv)!;
        toggle.Invoke(cut.Instance, null);
        cut.Render();
        Assert.Contains("task-tracker-empty", cut.Markup);
    }

    [Fact]
    public void PopoverOpen_ShowsTaskRows_WhenHasTasks()
    {
        var cut = Render<TaskTrackerWidget>();
        var tracker = Services.GetRequiredService<TaskTrackerService>();
        var byId = typeof(TaskTrackerService).GetField("_byId", Priv)!.GetValue(tracker) as Dictionary<int, ServerTaskDto>;
        byId![1] = new ServerTaskDto { Id = 1, ServerId = 5, ServerName = "web-server", Name = "TestTask", Status = TaskExecutionStatus.Running, CreatedAt = DateTime.UtcNow, Command = "echo hello", TimeoutSeconds = 60 };
        byId[2] = new ServerTaskDto { Id = 2, ServerId = 5, ServerName = "web-server", Name = "Task2", Status = TaskExecutionStatus.Pending, CreatedAt = DateTime.UtcNow, Command = "sleep", TimeoutSeconds = 60 };
        byId[3] = new ServerTaskDto { Id = 3, ServerId = 6, ServerName = "db-server", Name = "BackupTask", Status = TaskExecutionStatus.Assigned, CreatedAt = DateTime.UtcNow, Command = "backup", TimeoutSeconds = 60 };

        // Force count update
        WidgetType.GetField("_count", Priv)!.SetValue(cut.Instance, 3);
        var toggle = WidgetType.GetMethod("TogglePopover", Priv)!;
        toggle.Invoke(cut.Instance, null);
        cut.Render();
        Assert.Contains("task-tracker-row", cut.Markup);
    }

    [Fact]
    public void PopoverOpen_GroupsTasksByServer_WhenMoreThanFive()
    {
        var cut = Render<TaskTrackerWidget>();
        var tracker = Services.GetRequiredService<TaskTrackerService>();
        var byId = typeof(TaskTrackerService).GetField("_byId", Priv)!.GetValue(tracker) as Dictionary<int, ServerTaskDto>;
        for (int i = 1; i <= 6; i++)
        {
            byId![i] = new ServerTaskDto { Id = i, ServerId = i % 2 == 0 ? 10 : 20, ServerName = i % 2 == 0 ? "srv-a" : "srv-b", Name = $"Task{i}", Status = TaskExecutionStatus.Running, CreatedAt = DateTime.UtcNow, Command = "cmd", TimeoutSeconds = 60 };
        }
        WidgetType.GetField("_count", Priv)!.SetValue(cut.Instance, 6);
        var toggle = WidgetType.GetMethod("TogglePopover", Priv)!;
        toggle.Invoke(cut.Instance, null);
        cut.Render();
        Assert.Contains("task-tracker-group-header", cut.Markup);
    }

    [Fact]
    public void OnTrackerChanged_UpdatesCount()
    {
        var cut = Render<TaskTrackerWidget>();
        WidgetType.GetField("_count", Priv)!.SetValue(cut.Instance, 0);

        // Push two live tasks into the tracker so a real recalculation moves _count off 0 - proving
        // OnTrackerChanged re-reads Tracker.Count rather than being a no-op on an empty tracker.
        var tracker = Services.GetRequiredService<TaskTrackerService>();
        var byId = typeof(TaskTrackerService).GetField("_byId", Priv)!.GetValue(tracker) as Dictionary<int, ServerTaskDto>;
        byId![1] = new ServerTaskDto { Id = 1, ServerId = 10, ServerName = "srv1", Name = "Deploy", Status = TaskExecutionStatus.Running, CreatedAt = DateTime.UtcNow, Command = "cmd", TimeoutSeconds = 60 };
        byId[2] = new ServerTaskDto { Id = 2, ServerId = 10, ServerName = "srv1", Name = "Backup", Status = TaskExecutionStatus.Pending, CreatedAt = DateTime.UtcNow, Command = "cmd", TimeoutSeconds = 60 };

        var method = WidgetType.GetMethod("OnTrackerChanged", Priv)!;
        method.Invoke(cut.Instance, []);

        var count = (int)WidgetType.GetField("_count", Priv)!.GetValue(cut.Instance)!;
        Assert.Equal(2, count);
    }

    [Fact]
    public void Dispose_Unsubscribes_WithoutThrowing()
    {
        var cut = Render<TaskTrackerWidget>();
        cut.Instance.Dispose();
    }
}
