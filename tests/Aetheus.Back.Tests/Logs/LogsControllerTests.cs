// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Logs;
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class LogsControllerTests
{
    private readonly ILogService _serviceMock = Substitute.For<ILogService>();
    private readonly ITaskService _taskServiceMock = Substitute.For<ITaskService>();
    private readonly IResourceAuthorizationService _authzMock = Substitute.For<IResourceAuthorizationService>();
    private readonly LogsController _sut;

    public LogsControllerTests()
    {
        _taskServiceMock.GetTaskServerIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _taskServiceMock.GetServerIdsForTasksAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<IReadOnlyCollection<int>>(0).ToDictionary(id => id, _ => 1));
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<ResourceType>(), Arg.Any<int?>(), Arg.Any<Permission>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _sut = new LogsController(_serviceMock, _taskServiceMock, _authzMock, Substitute.For<IAuditService>());
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("ServerId", "1")], "test"))
            }
        };
    }

    [Fact]
    public async Task GetTaskLogs_ReturnsOkWithLogs()
    {
        var logs = new List<TaskLogDto>
        {
            new() { Id = 1, TaskId = 5, Level = TaskLogLevel.Info, Message = "ok" }
        };
        _serviceMock.GetTaskLogsAsync(5, Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(logs);

        var result = await _sut.GetTaskLogs(5, null, TestContext.Current.CancellationToken);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var data = Assert.IsType<List<TaskLogDto>>(okResult.Value);
        Assert.Single(data);
    }

    [Fact]
    public async Task GetTaskLogs_WithoutServerReadPermission_ForbidsAndDoesNotReadLogs()
    {
        // The task resolves to serverId 1 (ctor stub); deny Read on it and assert Forbid + no log read.
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Server, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.GetTaskLogs(5, null, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
        await _serviceMock.DidNotReceive().GetTaskLogsAsync(Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AppendLog_ReturnsOk()
    {
        var request = new AppendLogRequest { TaskId = 1, Level = TaskLogLevel.Info, Message = "test" };

        var result = await _sut.AppendLog(request, TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
        await _serviceMock.Received(1).AppendLogAsync(request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AppendLogs_ReturnsOk()
    {
        var requests = new List<AppendLogRequest>
        {
            new() { TaskId = 1, Level = TaskLogLevel.Info, Message = "a" },
            new() { TaskId = 1, Level = TaskLogLevel.Debug, Message = "b" }
        };

        var result = await _sut.AppendLogs(requests, TestContext.Current.CancellationToken);

        Assert.IsType<OkResult>(result);
        await _serviceMock.Received(1).AppendLogsAsync(requests, Arg.Any<CancellationToken>());
    }
}
