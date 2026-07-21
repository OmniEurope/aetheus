// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;
using Aetheus.Agent.Core.Operations;
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Aetheus.Agent.Core.Tests;

public class PollingServiceTests
{
    private readonly IServerApiClient _apiClientMock = Substitute.For<IServerApiClient>();
    private readonly IExecutor _executorMock = Substitute.For<IExecutor>();
    private readonly IAgentProcessRestarter _processRestarter = Substitute.For<IAgentProcessRestarter>();
    private readonly AgentState _agentState;
    private readonly EnrollmentService _enrollment;
    private readonly IOptions<AetheusAgentOptions> _options;

    public PollingServiceTests()
    {
        _agentState = new AgentState
        {
            ServerId = 1,
            BearerToken = "test-token"
        };

        _options = Options.Create(new AetheusAgentOptions
        {
            ServerUrl = "http://localhost:5301",
            PollingIntervalSeconds = 1,
            MaxConcurrentTasks = 2
        });

        _enrollment = CreateEnrolledEnrollmentService();

        _executorMock.Type.Returns(ExecutorType.Shell);
    }

    // E-3: drive one poll iteration and await the dispatched in-flight tasks so the
    // assertion on a task's terminal effect is deterministic (no PeriodicTimer / sleep).
    private static async Task PollAndDrainAsync(PollingService service)
    {
        await service.PollOnceAsync(TestContext.Current.CancellationToken);
        await service.DrainRunningTasksAsync();
    }

    private PollingService CreateService(
        IEnumerable<IOperationExecutor>? operationExecutors = null,
        IDockerStorageMaintenance? dockerStorageMaintenance = null) => new(
        _apiClientMock,
        _enrollment,
        [_executorMock],
        operationExecutors ?? Array.Empty<IOperationExecutor>(),
        Substitute.For<IContainerExecutor>(),
        dockerStorageMaintenance ?? Substitute.For<IDockerStorageMaintenance>(),
        _agentState,
        new AgentRuntimeHealth(TimeProvider.System),
        _processRestarter,
        TimeProvider.System,
        _options,
        NullLogger<PollingService>.Instance);

    [Fact]
    public async Task PollOnceAsync_PollsForTasksWhenEnrolled()
    {
        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingTaskDto>());

        var service = CreateService();
        await PollAndDrainAsync(service);

        await _apiClientMock.Received(1).GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PollOnceAsync_ExecutesReceivedTask()
    {
        var pendingTask = new PendingTaskDto
        {
            Id = 42,
            Name = "test-task",
            Command = "echo hello",
            Executor = ExecutorType.Shell,
            EnvironmentVariables = new Dictionary<string, string>(),
            TimeoutSeconds = 30
        };

        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingTaskDto> { pendingTask });

        _executorMock.ExecuteAsync(
                "echo hello",
                Arg.Any<Dictionary<string, string>>(),
                30,
                Arg.Any<Func<string, TaskLogLevel, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(new ExecutorResult(0, false));

        var service = CreateService();
        await PollAndDrainAsync(service);

        await _apiClientMock.Received(1).StartTaskAsync(42, Arg.Any<CancellationToken>());

        await _apiClientMock.Received(1).CompleteTaskAsync(42, Arg.Is<TaskResultDto>(r =>
                r.Status == TaskExecutionStatus.Success && r.ExitCode == 0),
                Arg.Any<CancellationToken>());
        Assert.Equal(_options.Value.WorkDirectory,
            pendingTask.EnvironmentVariables["AETHEUS_AGENT_WORK_DIRECTORY"]);
    }

    [Fact]
    public async Task PollOnceAsync_DockerBuild_HoldsMaintenanceLeaseAndFinalizesAfterFailure()
    {
        var maintenance = Substitute.For<IDockerStorageMaintenance>();
        maintenance.IsBuildCommand(Arg.Any<string>()).Returns(true);
        var pendingTask = new PendingTaskDto
        {
            Id = 43,
            Name = "container-build",
            Command = "docker buildx build --load .",
            Executor = ExecutorType.Shell,
            EnvironmentVariables = [],
            TimeoutSeconds = 30
        };
        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([pendingTask]);
        _executorMock.ExecuteAsync(
                pendingTask.Command, pendingTask.EnvironmentVariables, 30,
                Arg.Any<Func<string, TaskLogLevel, Task>>(), Arg.Any<CancellationToken>())
            .Returns(new ExecutorResult(1, false));

        await PollAndDrainAsync(CreateService(dockerStorageMaintenance: maintenance));

        await maintenance.Received(1).PrepareBuildAsync(
            pendingTask.EnvironmentVariables, Arg.Any<CancellationToken>(), true);
        await maintenance.Received(1).CompleteBuildAsync(
            Arg.Is<CancellationToken>(token => !token.CanBeCanceled), true);
    }

    [Fact]
    public async Task PollOnceAsync_StructuredBuildRole_HoldsSharedLeaseWithoutDockerMaintenance()
    {
        var maintenance = Substitute.For<IDockerStorageMaintenance>();
        maintenance.IsBuildCommand(Arg.Any<string>()).Returns(false);
        var pendingTask = new PendingTaskDto
        {
            Id = 44,
            PipelineRunId = 12,
            Name = "dotnet-build",
            Command = "dotnet build Aetheus.slnx -c Release",
            Executor = ExecutorType.Shell,
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["AETHEUS_EXECUTION_ROLE"] = "build"
            },
            TimeoutSeconds = 30
        };
        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([pendingTask]);
        _executorMock.ExecuteAsync(
                pendingTask.Command, pendingTask.EnvironmentVariables, 30,
                Arg.Any<Func<string, TaskLogLevel, Task>>(), Arg.Any<CancellationToken>())
            .Returns(new ExecutorResult(0, false));

        await PollAndDrainAsync(CreateService(dockerStorageMaintenance: maintenance));

        await maintenance.Received(1).PrepareBuildAsync(
            pendingTask.EnvironmentVariables, Arg.Any<CancellationToken>(), false);
        await maintenance.Received(1).CompleteBuildAsync(
            Arg.Is<CancellationToken>(token => !token.CanBeCanceled), false);
    }

    [Fact]
    public async Task PollOnceAsync_DeploymentOnlyAgent_RefusesPipelineTaskWithoutDeployRole()
    {
        var maintenance = Substitute.For<IDockerStorageMaintenance>();
        maintenance.DeploymentOnly.Returns(true);
        var pendingTask = new PendingTaskDto
        {
            Id = 45,
            PipelineRunId = 12,
            Name = "unclassified-stage",
            Command = "echo forbidden",
            Executor = ExecutorType.Shell,
            EnvironmentVariables = [],
            TimeoutSeconds = 30
        };
        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([pendingTask]);

        await PollAndDrainAsync(CreateService(dockerStorageMaintenance: maintenance));

        await _apiClientMock.Received(1).StartTaskAsync(45, Arg.Any<CancellationToken>());
        await _apiClientMock.Received(1).CompleteTaskAsync(45,
            Arg.Is<TaskResultDto>(result => result.Status == TaskExecutionStatus.Failed),
            Arg.Any<CancellationToken>());
        await _executorMock.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(), Arg.Any<Dictionary<string, string>>(), Arg.Any<int>(),
            Arg.Any<Func<string, TaskLogLevel, Task>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PollOnceAsync_DeploymentOnlyAgent_AllowsExplicitDeployRole()
    {
        var maintenance = Substitute.For<IDockerStorageMaintenance>();
        maintenance.DeploymentOnly.Returns(true);
        var pendingTask = new PendingTaskDto
        {
            Id = 46,
            PipelineRunId = 12,
            Name = "deploy-stage",
            Command = "docker compose up -d",
            Executor = ExecutorType.Shell,
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["AETHEUS_EXECUTION_ROLE"] = "deploy"
            },
            TimeoutSeconds = 30
        };
        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([pendingTask]);
        _executorMock.ExecuteAsync(
                pendingTask.Command, pendingTask.EnvironmentVariables, 30,
                Arg.Any<Func<string, TaskLogLevel, Task>>(), Arg.Any<CancellationToken>())
            .Returns(new ExecutorResult(0, false));

        await PollAndDrainAsync(CreateService(dockerStorageMaintenance: maintenance));

        await _executorMock.Received(1).ExecuteAsync(
            pendingTask.Command, pendingTask.EnvironmentVariables, 30,
            Arg.Any<Func<string, TaskLogLevel, Task>>(), Arg.Any<CancellationToken>());
        await _apiClientMock.Received(1).CompleteTaskAsync(46,
            Arg.Is<TaskResultDto>(result => result.Status == TaskExecutionStatus.Success),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PollOnceAsync_ReportsFailedTask()
    {
        var pendingTask = new PendingTaskDto
        {
            Id = 99,
            Name = "fail-task",
            Command = "exit 1",
            Executor = ExecutorType.Shell,
            EnvironmentVariables = new Dictionary<string, string>(),
            TimeoutSeconds = 30
        };

        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingTaskDto> { pendingTask });

        _executorMock.ExecuteAsync(
                "exit 1",
                Arg.Any<Dictionary<string, string>>(),
                30,
                Arg.Any<Func<string, TaskLogLevel, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(new ExecutorResult(1, false));

        var service = CreateService();
        await PollAndDrainAsync(service);

        await _apiClientMock.Received(1).CompleteTaskAsync(99, Arg.Is<TaskResultDto>(r =>
                r.Status == TaskExecutionStatus.Failed && r.ExitCode == 1),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PollOnceAsync_SuccessfulSelfUpdate_CompletesBackendTaskBeforeRestartingProcess()
    {
        var operation = Substitute.For<IOperationExecutor>();
        operation.CanHandle(OperationKind.AgentSelfUpdate).Returns(true);
        operation.ExecuteAsync(
                OperationKind.AgentSelfUpdate,
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<int>(),
                Arg.Any<Func<string, TaskLogLevel, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(new ExecutorResult(0, false));

        var completionObserved = false;
        _apiClientMock.CompleteTaskAsync(120, Arg.Any<TaskResultDto>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                completionObserved = true;
                return Task.CompletedTask;
            });
        _processRestarter.When(x => x.Restart(Arg.Any<string>()))
            .Do(_ => Assert.True(completionObserved));

        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new PendingTaskDto
                {
                    Id = 120,
                    Name = "self-update",
                    Operation = OperationKind.AgentSelfUpdate,
                    EnvironmentVariables = [],
                    TimeoutSeconds = 60
                }
            ]);

        await PollAndDrainAsync(CreateService([operation]));

        await _apiClientMock.Received(1).CompleteTaskAsync(120,
            Arg.Is<TaskResultDto>(result => result.Status == TaskExecutionStatus.Success),
            CancellationToken.None);
        _processRestarter.Received(1).Restart(Arg.Any<string>());
    }

    [Fact]
    public async Task PollOnceAsync_FailedSelfUpdate_DoesNotRestartProcess()
    {
        var operation = Substitute.For<IOperationExecutor>();
        operation.CanHandle(OperationKind.AgentSelfUpdate).Returns(true);
        operation.ExecuteAsync(
                OperationKind.AgentSelfUpdate,
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<int>(),
                Arg.Any<Func<string, TaskLogLevel, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(new ExecutorResult(1, false));
        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new PendingTaskDto
                {
                    Id = 121,
                    Name = "self-update",
                    Operation = OperationKind.AgentSelfUpdate,
                    EnvironmentVariables = [],
                    TimeoutSeconds = 60
                }
            ]);

        await PollAndDrainAsync(CreateService([operation]));

        _processRestarter.DidNotReceive().Restart(Arg.Any<string>());
    }

    [Fact]
    public async Task PollOnceAsync_SuccessfulSelfUpdate_RestartsEvenWhenFinalAcknowledgementFails()
    {
        var operation = Substitute.For<IOperationExecutor>();
        operation.CanHandle(OperationKind.AgentSelfUpdate).Returns(true);
        operation.ExecuteAsync(
                OperationKind.AgentSelfUpdate,
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<int>(),
                Arg.Any<Func<string, TaskLogLevel, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(new ExecutorResult(0, false));
        _apiClientMock.CompleteTaskAsync(122, Arg.Any<TaskResultDto>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("control plane unavailable"));
        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new PendingTaskDto
                {
                    Id = 122,
                    Name = "self-update",
                    Operation = OperationKind.AgentSelfUpdate,
                    EnvironmentVariables = [],
                    TimeoutSeconds = 60
                }
            ]);

        await PollAndDrainAsync(CreateService([operation]));

        _processRestarter.Received(1).Restart(Arg.Any<string>());
    }

    [Fact]
    public async Task PollOnceAsync_ApiFailure_DoesNotCorruptNextPoll()
    {
        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new HttpRequestException("Server unreachable"),
                _ => new List<PendingTaskDto>());

        var service = CreateService();

        // First poll throws (the loop swallows it and retries next tick); the SECOND poll
        // must still reach the server. Two real iterations replace "sleep 4s, hope ≥2 calls".
        await Assert.ThrowsAsync<HttpRequestException>(() => service.PollOnceAsync(TestContext.Current.CancellationToken));
        await service.PollOnceAsync(TestContext.Current.CancellationToken);

        await _apiClientMock.Received(2).GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PollOnceAsync_TaskReportFailure_DoesNotCorruptNextPoll()
    {
        var pendingTask = new PendingTaskDto
        {
            Id = 50,
            Name = "report-fail-task",
            Command = "echo ok",
            Executor = ExecutorType.Shell,
            EnvironmentVariables = new Dictionary<string, string>(),
            TimeoutSeconds = 30
        };

        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingTaskDto> { pendingTask }, new List<PendingTaskDto>());

        _executorMock.ExecuteAsync(
                Arg.Any<string>(),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<int>(),
                Arg.Any<Func<string, TaskLogLevel, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(new ExecutorResult(0, false));

        _apiClientMock.CompleteTaskAsync(50, Arg.Any<TaskResultDto>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Failed to report"));

        var service = CreateService();

        // The completion report throws inside the dispatched task; the failure is contained
        // (does not bubble out of the poll loop) and the next poll still runs.
        await PollAndDrainAsync(service);
        await service.PollOnceAsync(TestContext.Current.CancellationToken);

        await _apiClientMock.Received().CompleteTaskAsync(50, Arg.Any<TaskResultDto>(), Arg.Any<CancellationToken>());
        await _apiClientMock.Received(2).GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_NotEnrolled_DoesNotPoll()
    {
        var notEnrolledState = new AgentState();
        var notEnrolled = new EnrollmentService(
            _apiClientMock,
            _options,
            notEnrolledState,
            Substitute.For<Microsoft.Extensions.Configuration.IConfiguration>(),
            Substitute.For<ICredentialProtector>(),
            Substitute.For<IShellRunner>(),
            TimeProvider.System,
            NullLogger<EnrollmentService>.Instance);

        var service = new PollingService(
            _apiClientMock,
            notEnrolled,
            [_executorMock],
            Array.Empty<IOperationExecutor>(),
            Substitute.For<IContainerExecutor>(),
            Substitute.For<IDockerStorageMaintenance>(),
            notEnrolledState,
            new AgentRuntimeHealth(TimeProvider.System),
            _processRestarter,
            TimeProvider.System,
            _options,
            NullLogger<PollingService>.Instance);

        // While not enrolled the loop parks on its wait-gate and never reaches a poll.
        // Start then immediately stop: the gate is never crossed - deterministic, no sleep.
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await cts.CancelAsync();
        await service.StopAsync(TestContext.Current.CancellationToken);

        await _apiClientMock.DidNotReceive().GetPendingTasksAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private EnrollmentService CreateEnrolledEnrollmentService()
    {
        var mockConfig = Substitute.For<Microsoft.Extensions.Configuration.IConfiguration>();
        return new EnrollmentService(
            _apiClientMock,
            _options,
            _agentState,
            mockConfig,
            Substitute.For<ICredentialProtector>(),
            Substitute.For<IShellRunner>(),
            TimeProvider.System,
            NullLogger<EnrollmentService>.Instance);
    }

    [Fact]
    public async Task PollOnceAsync_NoExecutorForType_ReportsFailedImmediately()
    {
        var pendingTask = new PendingTaskDto
        {
            Id = 77,
            Name = "docker-task",
            Command = "run container",
            Executor = ExecutorType.Docker,
            EnvironmentVariables = new Dictionary<string, string>(),
            TimeoutSeconds = 30
        };

        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingTaskDto> { pendingTask });

        var service = CreateService();
        await PollAndDrainAsync(service);

        await _apiClientMock.Received(1).CompleteTaskAsync(77,
            Arg.Is<TaskResultDto>(r => r.Status == TaskExecutionStatus.Failed && r.ExitCode == -1),
            Arg.Any<CancellationToken>());

        await _apiClientMock.Received(1).StartTaskAsync(77, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PollOnceAsync_TimedOutTask_ReportsTimeout()
    {
        var pendingTask = new PendingTaskDto
        {
            Id = 88,
            Name = "slow-task",
            Command = "sleep 999",
            Executor = ExecutorType.Shell,
            EnvironmentVariables = new Dictionary<string, string>(),
            TimeoutSeconds = 1
        };

        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingTaskDto> { pendingTask });

        _executorMock.ExecuteAsync(
                "sleep 999", Arg.Any<Dictionary<string, string>>(), 1,
                Arg.Any<Func<string, TaskLogLevel, Task>>(), Arg.Any<CancellationToken>())
            .Returns(new ExecutorResult(-1, true));

        var service = CreateService();
        await PollAndDrainAsync(service);

        await _apiClientMock.Received(1).CompleteTaskAsync(88,
            Arg.Is<TaskResultDto>(r => r.Status == TaskExecutionStatus.Timeout),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PollOnceAsync_OperationTask_UsesOperationExecutor()
    {
        var operationExecutorMock = Substitute.For<IOperationExecutor>();
        operationExecutorMock.CanHandle(OperationKind.DockerRestartContainer).Returns(true);
        // Item #5.3: setup the env-var-aware overload that PollingService now calls.
        operationExecutorMock.ExecuteAsync(
                OperationKind.DockerRestartContainer, "nginx",
                Arg.Any<IReadOnlyDictionary<string, string>>(), 30,
                Arg.Any<Func<string, TaskLogLevel, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(new ExecutorResult(0, false));

        var pendingTask = new PendingTaskDto
        {
            Id = 100,
            Name = "restart-container",
            Command = "nginx",
            Operation = OperationKind.DockerRestartContainer,
            Executor = ExecutorType.Shell,
            EnvironmentVariables = new Dictionary<string, string>(),
            TimeoutSeconds = 30
        };

        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingTaskDto> { pendingTask });

        var service = CreateService([operationExecutorMock]);
        await PollAndDrainAsync(service);

        await _apiClientMock.Received(1).StartTaskAsync(100, Arg.Any<CancellationToken>());
        // Item #5.3: PollingService now passes env vars to the typed-op executor (new overload).
        await operationExecutorMock.Received(1).ExecuteAsync(
            OperationKind.DockerRestartContainer, "nginx",
            Arg.Any<IReadOnlyDictionary<string, string>>(), 30,
            Arg.Any<Func<string, TaskLogLevel, Task>>(),
            Arg.Any<CancellationToken>());
        await _apiClientMock.Received(1).CompleteTaskAsync(100,
            Arg.Is<TaskResultDto>(r => r.Status == TaskExecutionStatus.Success),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PollOnceAsync_OperationTask_NoHandler_ReportsFailure()
    {
        var pendingTask = new PendingTaskDto
        {
            Id = 101,
            Name = "unknown-operation",
            Command = "target",
            Operation = OperationKind.DockerRestartContainer,
            Executor = ExecutorType.Shell,
            EnvironmentVariables = new Dictionary<string, string>(),
            TimeoutSeconds = 30
        };

        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingTaskDto> { pendingTask });

        var service = CreateService();
        await PollAndDrainAsync(service);

        await _apiClientMock.Received(1).CompleteTaskAsync(101,
            Arg.Is<TaskResultDto>(r => r.Status == TaskExecutionStatus.Failed && r.ExitCode == -1),
            Arg.Any<CancellationToken>());
        await _apiClientMock.Received(1).StartTaskAsync(101, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PollOnceAsync_OperationTask_Failed_ReportsFailedStatus()
    {
        var operationExecutorMock = Substitute.For<IOperationExecutor>();
        operationExecutorMock.CanHandle(OperationKind.ServiceRestart).Returns(true);
        operationExecutorMock.ExecuteAsync(
                OperationKind.ServiceRestart, "nginx",
                Arg.Any<IReadOnlyDictionary<string, string>>(), 30,
                Arg.Any<Func<string, TaskLogLevel, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(new ExecutorResult(1, false));

        var pendingTask = new PendingTaskDto
        {
            Id = 102,
            Name = "restart-service",
            Command = "nginx",
            Operation = OperationKind.ServiceRestart,
            Executor = ExecutorType.Shell,
            EnvironmentVariables = new Dictionary<string, string>(),
            TimeoutSeconds = 30
        };

        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingTaskDto> { pendingTask });

        var service = CreateService([operationExecutorMock]);
        await PollAndDrainAsync(service);

        await _apiClientMock.Received(1).CompleteTaskAsync(102,
            Arg.Is<TaskResultDto>(r => r.Status == TaskExecutionStatus.Failed && r.ExitCode == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PollOnceAsync_ExecutorThrows_ReportsFailedAndContinues()
    {
        var pendingTask = new PendingTaskDto
        {
            Id = 66,
            Name = "crash-task",
            Command = "crash",
            Executor = ExecutorType.Shell,
            EnvironmentVariables = new Dictionary<string, string>(),
            TimeoutSeconds = 30
        };

        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingTaskDto> { pendingTask });

        _executorMock.ExecuteAsync(
                "crash", Arg.Any<Dictionary<string, string>>(), 30,
                Arg.Any<Func<string, TaskLogLevel, Task>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Executor crashed"));

        var service = CreateService();
        await PollAndDrainAsync(service);

        await _apiClientMock.Received(1).CompleteTaskAsync(66,
            Arg.Is<TaskResultDto>(r => r.Status == TaskExecutionStatus.Failed && r.ExitCode == -1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PollOnceAsync_EnforcesMaximumConcurrency_AndReleasesGateAfterFailure()
    {
        var pending = Enumerable.Range(1, 3).Select(id => new PendingTaskDto
        {
            Id = id,
            Name = $"blocked-{id}",
            Command = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Executor = ExecutorType.Shell,
            EnvironmentVariables = new Dictionary<string, string>(),
            TimeoutSeconds = 30
        }).ToArray();
        _apiClientMock.GetPendingTasksAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([.. pending], [pending[2]]);
        var releases = Enumerable.Range(0, 4)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var started = Enumerable.Range(0, 4)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var current = 0;
        var maximum = 0;
        _executorMock.ExecuteAsync(
                Arg.Any<string>(), Arg.Any<Dictionary<string, string>>(), Arg.Any<int>(),
                Arg.Any<Func<string, TaskLogLevel, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => ExecuteBlockedAsync(call.ArgAt<string>(0)));
        var service = CreateService();

        await service.PollOnceAsync(TestContext.Current.CancellationToken);
        await Task.WhenAll(started[1].Task, started[2].Task)
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.False(started[3].Task.IsCompleted);
        Assert.Equal(2, Volatile.Read(ref maximum));
        Assert.Equal(2, service._runningTasks.Count);

        releases[1].TrySetResult();
        await WaitUntilAsync(() => !service._runningTasks.ContainsKey(1));
        await service.PollOnceAsync(TestContext.Current.CancellationToken);
        await started[3].Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(2, Volatile.Read(ref maximum));
        releases[2].TrySetResult();
        releases[3].TrySetResult();
        await service.DrainRunningTasksAsync();
        Assert.Equal(0, Volatile.Read(ref current));

        async Task<ExecutorResult> ExecuteBlockedAsync(string command)
        {
            var id = int.Parse(command, System.Globalization.CultureInfo.InvariantCulture);
            var active = Interlocked.Increment(ref current);
            UpdateMaximum(ref maximum, active);
            started[id].TrySetResult();
            try
            {
                await releases[id].Task;
                if (id == 1)
                    throw new InvalidOperationException("intentional executor failure");
                return new ExecutorResult(0, false);
            }
            finally
            {
                Interlocked.Decrement(ref current);
            }
        }
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        int observed;
        do
        {
            observed = Volatile.Read(ref maximum);
            if (candidate <= observed) return;
        } while (Interlocked.CompareExchange(ref maximum, candidate, observed) != observed);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);

        Assert.True(condition(), "The asynchronous polling state did not reach the expected condition.");
    }

    // Reconciliation: a task reported terminal server-side is cancelled locally - this is the trigger
    // that unwinds ExecuteTaskAsync, whose finally releases the concurrency gate. We assert the
    // cancellation (the reconcile responsibility); the unconditional gate release is the C# finally.
    [Fact]
    public async Task ReconcileRunningTasksAsync_TerminalStatus_CancelsTrackedTask()
    {
        var service = CreateService();
        var cts = new CancellationTokenSource();
        service._runningTasks[42] = new PollingService.TrackedTask(Task.CompletedTask, cts);
        _apiClientMock.GetTaskStatusesAsync(Arg.Any<List<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, string> { [42] = nameof(TaskExecutionStatus.Cancelled) });

        await service.ReconcileRunningTasksAsync(TestContext.Current.CancellationToken);

        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public async Task ReconcileRunningTasksAsync_NonTerminalStatus_DoesNotCancel()
    {
        var service = CreateService();
        var cts = new CancellationTokenSource();
        service._runningTasks[42] = new PollingService.TrackedTask(Task.CompletedTask, cts);
        _apiClientMock.GetTaskStatusesAsync(Arg.Any<List<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, string> { [42] = nameof(TaskExecutionStatus.Running) });

        await service.ReconcileRunningTasksAsync(TestContext.Current.CancellationToken);

        Assert.False(cts.IsCancellationRequested);
    }
}
