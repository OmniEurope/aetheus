// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Tasks;
using Aetheus.Front.Components.Users;
using UsersPage = Aetheus.Front.Components.Users.Users;

namespace Aetheus.Front.Tests.Pages;

/// <summary>
/// Tests for static helper methods across various page components.
/// </summary>
public class StaticMethodBatchTests
{
    // --- TaskListView.GetTaskBadge ---

    [Theory]
    [InlineData(TaskExecutionStatus.Success, OmniTone.Success)]
    [InlineData(TaskExecutionStatus.Failed, OmniTone.Danger)]
    [InlineData(TaskExecutionStatus.Timeout, OmniTone.Danger)]
    [InlineData(TaskExecutionStatus.Running, OmniTone.Accent)]
    [InlineData(TaskExecutionStatus.Cancelled, OmniTone.Warning)]
    [InlineData(TaskExecutionStatus.Pending, OmniTone.Neutral)]
    [InlineData(TaskExecutionStatus.Assigned, OmniTone.Neutral)]
    public void Tasks_GetTaskBadge(TaskExecutionStatus status, OmniTone expected)
    {
        Assert.Equal(expected, TaskListView.GetTaskBadge(status));
    }

    // --- Users.GetRoleBadgeStyle ---

    [Theory]
    [InlineData("Admin", OmniTone.Danger)]
    [InlineData("admin", OmniTone.Danger)]
    [InlineData("Contributor", OmniTone.Accent)]
    [InlineData("contributor", OmniTone.Accent)]
    [InlineData("Reader", OmniTone.Accent)]
    [InlineData("reader", OmniTone.Accent)]
    [InlineData("CustomRole", OmniTone.Neutral)]
    [InlineData("", OmniTone.Neutral)]
    public void Users_GetRoleBadgeStyle(string role, OmniTone expected)
    {
        var method = typeof(UsersPage).GetMethod("GetRoleBadgeStyle",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var result = (OmniTone)method.Invoke(null, [role])!;
        Assert.Equal(expected, result);
    }
}
