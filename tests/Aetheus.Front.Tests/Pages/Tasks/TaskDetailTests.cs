// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Tasks;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Tasks;

public class TaskDetailTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public TaskDetailTests() => _handler = BunitTestHelper.RegisterServices(this);

    [Fact]
    public void RendersFailureDetailsAndLogs()
    {
        _handler.SetJsonResponse("api/tasks/7085", new ServerTaskDto
        {
            Id = 7085,
            ServerId = 9,
            ServerName = "vps2577917",
            Name = "Agent self-update to 1.0.1294",
            Command = "1.0.1294",
            Executor = ExecutorType.Operation,
            Status = TaskExecutionStatus.Failed,
            CreatedAt = new DateTime(2026, 8, 5, 7, 40, 7, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 8, 5, 7, 40, 8, DateTimeKind.Utc),
            ExitCode = -1,
            FailureCode = TaskFailureCodes.InfrastructureMismatch,
            FailureReason = "Linux integration posture upgrade supervisor is missing or obsolete.",
            TimeoutSeconds = 900
        });
        _handler.SetJsonResponse("api/logs/task/7085", new List<TaskLogDto>
        {
            new()
            {
                Id = 1,
                TaskId = 7085,
                Level = TaskLogLevel.Error,
                Message = "Self-update refused: the installed Linux integration posture does not support autonomous full upgrades.",
                Timestamp = new DateTime(2026, 8, 5, 7, 40, 8, DateTimeKind.Utc)
            }
        });

        var cut = Render<TaskDetail>(parameters => parameters.Add(p => p.TaskId, 7085));
        cut.WaitForState(() => cut.Markup.Contains("Agent self-update to 1.0.1294"), TimeSpan.FromSeconds(2));

        Assert.Contains("Task #7085", cut.Markup);
        Assert.Contains("InfrastructureMismatch", cut.Markup);
        Assert.Contains("Linux integration posture upgrade supervisor is missing or obsolete.", cut.Markup);
        Assert.Contains("Self-update refused", cut.Markup);
        Assert.Contains("Operation", cut.Markup);
        Assert.DoesNotContain("Enum_ExecutorType_Operation", cut.Markup);
        Assert.Contains("href=\"/servers/9/overview\"", cut.Markup);
        Assert.DoesNotContain("CancelTask", cut.Markup);
    }
}
