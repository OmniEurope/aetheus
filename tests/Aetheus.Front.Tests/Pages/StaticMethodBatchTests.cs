// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Tasks;
using Aetheus.Front.Pages.Users;
using Aetheus.Shared.Enums;
using Radzen;
using UsersPage = Aetheus.Front.Pages.Users.Users;

namespace Aetheus.Front.Tests.Pages;

/// <summary>
/// Tests for static helper methods across various page components.
/// </summary>
public class StaticMethodBatchTests
{
    // --- TaskListView.GetTaskBadge ---

    [Theory]
    [InlineData(TaskExecutionStatus.Success, BadgeStyle.Success)]
    [InlineData(TaskExecutionStatus.Failed, BadgeStyle.Danger)]
    [InlineData(TaskExecutionStatus.Timeout, BadgeStyle.Danger)]
    [InlineData(TaskExecutionStatus.Running, BadgeStyle.Info)]
    [InlineData(TaskExecutionStatus.Cancelled, BadgeStyle.Warning)]
    [InlineData(TaskExecutionStatus.Pending, BadgeStyle.Light)]
    [InlineData(TaskExecutionStatus.Assigned, BadgeStyle.Light)]
    public void Tasks_GetTaskBadge(TaskExecutionStatus status, BadgeStyle expected)
    {
        Assert.Equal(expected, TaskListView.GetTaskBadge(status));
    }

    // --- Users.GetRoleBadgeStyle ---

    [Theory]
    [InlineData("Admin", BadgeStyle.Danger)]
    [InlineData("admin", BadgeStyle.Danger)]
    [InlineData("Contributor", BadgeStyle.Primary)]
    [InlineData("contributor", BadgeStyle.Primary)]
    [InlineData("Reader", BadgeStyle.Info)]
    [InlineData("reader", BadgeStyle.Info)]
    [InlineData("CustomRole", BadgeStyle.Light)]
    [InlineData("", BadgeStyle.Light)]
    public void Users_GetRoleBadgeStyle(string role, BadgeStyle expected)
    {
        var method = typeof(UsersPage).GetMethod("GetRoleBadgeStyle",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var result = (BadgeStyle)method.Invoke(null, [role])!;
        Assert.Equal(expected, result);
    }
}
