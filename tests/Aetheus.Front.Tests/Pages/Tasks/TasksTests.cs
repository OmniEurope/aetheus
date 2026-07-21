// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Tasks;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Radzen;
using TasksPage = Aetheus.Front.Pages.Tasks.Tasks;

namespace Aetheus.Front.Tests.Pages;

public class TasksTests : BunitContext
{
    public TasksTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private readonly BunitTestHelper.TestHandler _handler;

    [Fact]
    public void Renders_TaskGrid()
    {
        _handler.SetJsonResponse("api/tasks", new PaginatedResult<ServerTaskDto>
        {
            Items =
            [
                new ServerTaskDto
                {
                    Id = 1,
                    Name = "deploy-app",
                    ServerName = "srv1",
                    Executor = ExecutorType.Shell,
                    Status = TaskExecutionStatus.Success,
                    CreatedAt = DateTime.UtcNow,
                    ExitCode = 0
                }
            ],
            TotalCount = 1
        });

        var cut = Render<TasksPage>();
        // RadzenDataGrid LoadData requires JS interop; verify page renders without error
        Assert.Contains("Tasks", cut.Markup);
    }

    [Fact]
    public void Renders_EmptyGrid()
    {
        _handler.SetJsonResponse("api/tasks", new PaginatedResult<ServerTaskDto>
        {
            Items = [],
            TotalCount = 0
        });

        var cut = Render<TasksPage>();
        Assert.Contains("Tasks", cut.Markup);
    }

    [Fact]
    public void ViewLogs_SetupRendersWithoutError()
    {
        _handler.SetJsonResponse("api/tasks", new PaginatedResult<ServerTaskDto>
        {
            Items = [new ServerTaskDto { Id = 5, Name = "test", Status = TaskExecutionStatus.Success }],
            TotalCount = 1
        });
        _handler.SetJsonResponse("api/logs/task/5", new List<TaskLogDto>
        {
            new() { Id = 1, TaskId = 5, Message = "log line 1", Level = TaskLogLevel.Info, Timestamp = DateTime.UtcNow }
        });

        var cut = Render<TasksPage>();
        // RadzenDataGrid LoadData requires JS interop; verify page renders
        Assert.Contains("Tasks", cut.Markup);
    }

    // Status-badge styling now lives on the shared TaskListView component used by both task views.
    [Theory]
    [InlineData(TaskExecutionStatus.Success, BadgeStyle.Success)]
    [InlineData(TaskExecutionStatus.Failed, BadgeStyle.Danger)]
    [InlineData(TaskExecutionStatus.Timeout, BadgeStyle.Danger)]
    [InlineData(TaskExecutionStatus.Running, BadgeStyle.Info)]
    [InlineData(TaskExecutionStatus.Cancelled, BadgeStyle.Warning)]
    [InlineData(TaskExecutionStatus.Pending, BadgeStyle.Light)]
    [InlineData(TaskExecutionStatus.Assigned, BadgeStyle.Light)]
    public void GetTaskBadge_ReturnsExpectedStyle(TaskExecutionStatus status, BadgeStyle expected)
    {
        var result = TaskListView.GetTaskBadge(status);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Renders_MultipleTasksWithVariousStatuses()
    {
        _handler.SetJsonResponse("api/tasks", new PaginatedResult<ServerTaskDto>
        {
            Items =
            [
                new ServerTaskDto { Id = 1, Name = "deploy", ServerName = "srv1", Executor = ExecutorType.Shell, Status = TaskExecutionStatus.Success, CreatedAt = DateTime.UtcNow, ExitCode = 0 },
                new ServerTaskDto { Id = 2, Name = "backup", ServerName = "srv2", Executor = ExecutorType.Docker, Status = TaskExecutionStatus.Failed, CreatedAt = DateTime.UtcNow, ExitCode = 1 },
                new ServerTaskDto { Id = 3, Name = "test", ServerName = "srv1", Executor = ExecutorType.Shell, Status = TaskExecutionStatus.Running, CreatedAt = DateTime.UtcNow },
                new ServerTaskDto { Id = 4, Name = "cleanup", ServerName = "srv3", Executor = ExecutorType.Shell, Status = TaskExecutionStatus.Cancelled, CreatedAt = DateTime.UtcNow },
                new ServerTaskDto { Id = 5, Name = "build", ServerName = "srv1", Executor = ExecutorType.Shell, Status = TaskExecutionStatus.Timeout, CreatedAt = DateTime.UtcNow }
            ],
            TotalCount = 5
        });

        var cut = Render<TasksPage>();
        Assert.Contains("Tasks", cut.Markup);
    }

    [Fact]
    public void Renders_TaskWithLogs()
    {
        _handler.SetJsonResponse("api/tasks", new PaginatedResult<ServerTaskDto>
        {
            Items =
            [
                new ServerTaskDto { Id = 10, Name = "deploy-v2", ServerName = "prod-01", Executor = ExecutorType.Shell, Status = TaskExecutionStatus.Success, CreatedAt = DateTime.UtcNow, ExitCode = 0, Command = "deploy.sh" }
            ],
            TotalCount = 1
        });
        _handler.SetJsonResponse("api/logs/task/10", new List<TaskLogDto>
        {
            new() { Id = 1, TaskId = 10, Message = "Starting deploy...", Level = TaskLogLevel.Info, Timestamp = DateTime.UtcNow },
            new() { Id = 2, TaskId = 10, Message = "Deploy complete", Level = TaskLogLevel.Info, Timestamp = DateTime.UtcNow },
            new() { Id = 3, TaskId = 10, Message = "Warning: stale locks", Level = TaskLogLevel.Warning, Timestamp = DateTime.UtcNow }
        });

        var cut = Render<TasksPage>();
        Assert.Contains("Tasks", cut.Markup);
    }

    [Fact]
    public async Task ViewLogs_SetsSelectedTaskLogs()
    {
        _handler.SetJsonResponse("api/tasks", new PaginatedResult<ServerTaskDto>
        {
            Items = [new ServerTaskDto { Id = 7, Name = "deploy", Status = TaskExecutionStatus.Success }],
            TotalCount = 1
        });
        _handler.SetJsonResponse("api/logs/task/7", new List<TaskLogDto>
        {
            new() { Id = 1, TaskId = 7, Message = "done", Level = TaskLogLevel.Info, Timestamp = DateTime.UtcNow }
        });

        var cut = Render<TaskListView>();

        await cut.InvokeAsync(async () => await cut.Instance.ViewLogs(7));

        Assert.Equal(7, cut.Instance._selectedTaskId);
    }
}
