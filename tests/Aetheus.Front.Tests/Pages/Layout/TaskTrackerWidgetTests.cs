// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Layout;
using Aetheus.Front.Services;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Front.Tests.Pages;

public class TaskTrackerWidgetTests : BunitContext
{
    public TaskTrackerWidgetTests()
    {
        // Unauthenticated: OnInitializedAsync skips Tracker.StartAsync, so no live hub connect.
        BunitTestHelper.RegisterServices(this, authenticated: false);
        Services.AddScoped(sp => new TaskTrackerService(
            sp.GetRequiredService<ApiClient>(),
            sp.GetRequiredService<AuthStateProvider>(),
            sp.GetRequiredService<HubConnectionFactory>(),
            NullLogger<TaskTrackerService>.Instance));
    }

    [Fact]
    public void Renders_Button_WhenUnauthenticated()
    {
        var cut = Render<TaskTrackerWidget>();
        Assert.NotEmpty(cut.FindAll(".task-tracker-btn"));
    }

    [Fact]
    public void TogglePopover_RevealsEmptyPopover()
    {
        var cut = Render<TaskTrackerWidget>();
        var toggle = typeof(TaskTrackerWidget).GetMethod("TogglePopover", BindingFlags.NonPublic | BindingFlags.Instance)!;

        toggle.Invoke(cut.Instance, null);
        cut.Render();

        Assert.NotEmpty(cut.FindAll(".task-tracker-popover"));
        Assert.NotEmpty(cut.FindAll(".task-tracker-empty"));
    }

    [Theory]
    [InlineData(TaskExecutionStatus.Running, "play_arrow")]
    [InlineData(TaskExecutionStatus.Assigned, "schedule")]
    [InlineData(TaskExecutionStatus.Pending, "hourglass_top")]
    [InlineData(TaskExecutionStatus.Success, "task_alt")]
    public void StatusIcon_ReturnsExpected(TaskExecutionStatus status, string expected)
    {
        var m = typeof(TaskTrackerWidget).GetMethod("StatusIcon", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(expected, (string)m.Invoke(null, [status])!);
    }

    [Theory]
    [InlineData(TaskExecutionStatus.Running, "task-tracker-icon-running")]
    [InlineData(TaskExecutionStatus.Assigned, "task-tracker-icon-assigned")]
    [InlineData(TaskExecutionStatus.Pending, "task-tracker-icon-pending")]
    public void StatusIconClass_ReturnsExpected(TaskExecutionStatus status, string expected)
    {
        var m = typeof(TaskTrackerWidget).GetMethod("StatusIconClass", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(expected, (string)m.Invoke(null, [status])!);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var cut = Render<TaskTrackerWidget>();
        cut.Instance.Dispose();
    }
}
