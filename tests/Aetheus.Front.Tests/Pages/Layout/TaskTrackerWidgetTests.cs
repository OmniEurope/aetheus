// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Layout;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OmniEurope.Blazor.Components;

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
    public void TrackerLifecycle_IsNotStartedByWidget()
    {
        var tracker = Services.GetRequiredService<TaskTrackerService>();

        Render<TaskTrackerWidget>();

        Assert.False(tracker.IsInitialized);
    }

    [Fact]
    public void Renders_Button_WhenUnauthenticated()
    {
        var cut = Render<TaskTrackerWidget>();
        Assert.NotEmpty(cut.FindAll(".task-tracker-btn"));
    }

    [Fact]
    public void TriggerClick_RevealsEmptyPopoverPanel()
    {
        var cut = Render<TaskTrackerWidget>();

        cut.Find("button#task-tracker.task-tracker-btn").Click();

        Assert.Single(cut.FindAll("#task-tracker-panel"));
        Assert.NotEmpty(cut.FindAll("#task-tracker-panel .task-tracker-popover"));
        Assert.NotEmpty(cut.FindAll(".task-tracker-empty"));
    }

    [Theory]
    [InlineData(TaskExecutionStatus.Running, OmniIconName.Play)]
    [InlineData(TaskExecutionStatus.Assigned, OmniIconName.Timer)]
    [InlineData(TaskExecutionStatus.Pending, OmniIconName.Hourglass)]
    [InlineData(TaskExecutionStatus.Success, OmniIconName.CheckCircle)]
    public void StatusIcon_ReturnsExpected(TaskExecutionStatus status, OmniIconName expected)
    {
        var m = typeof(TaskTrackerWidget).GetMethod("StatusIcon", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(expected, (OmniIconName)m.Invoke(null, [status])!);
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
