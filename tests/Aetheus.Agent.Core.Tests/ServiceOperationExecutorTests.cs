// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;
using Aetheus.Agent.Core.Operations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Tests;

public class ServiceOperationExecutorTests
{
    private static ServiceOperationExecutor Build(
        bool isLinux = false,
        bool isWindows = true,
        Func<ProcessStartInfo, int, Func<string, TaskLogLevel, Task>, ILogger, CancellationToken, Task<ExecutorResult>>? runner = null) =>
        new(Options.Create(new AetheusAgentOptions()), NullLogger<ServiceOperationExecutor>.Instance)
        {
            IsLinux = isLinux,
            IsWindows = isWindows,
            RunProcessAsync = runner ?? ((_, _, _, _, _) => Task.FromResult(new ExecutorResult(0, false)))
        };

    private static Task NoOutput(string _, TaskLogLevel __) => Task.CompletedTask;

    [Theory]
    [InlineData(OperationKind.ServiceStart, true)]
    [InlineData(OperationKind.ServiceStop, true)]
    [InlineData(OperationKind.ServiceRestart, true)]
    [InlineData(OperationKind.ServiceStatus, true)]
    [InlineData(OperationKind.ServiceGetLogs, true)]
    [InlineData(OperationKind.ServiceEnable, true)]
    [InlineData(OperationKind.CronSave, false)]
    [InlineData(OperationKind.PortsentryUnblock, false)]
    public void CanHandle_OnlyServiceKinds(OperationKind kind, bool expected)
        => Assert.Equal(expected, Build().CanHandle(kind));

    [Fact]
    public async Task ExecuteAsync_InvalidServiceName_ReturnsFailureWithoutSpawning()
    {
        var messages = new List<string>();
        var result = await Build().ExecuteAsync(
            OperationKind.ServiceStart, "bad name with spaces", 30,
            (m, _) => { messages.Add(m); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains(messages, m => m.Contains("Invalid service name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_ServiceEnableOnWindows_ReturnsNotSupported()
    {
        var messages = new List<string>();
        var result = await Build().ExecuteAsync(
            OperationKind.ServiceEnable, "nginx", 30,
            (m, _) => { messages.Add(m); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains(messages, m => m.Contains("only supported on Linux", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_ServiceEnableOnLinux_ExecutesExactSudoArgv()
    {
        ProcessStartInfo? captured = null;
        var calls = 0;
        var executor = Build(
            isLinux: true,
            isWindows: false,
            runner: (startInfo, _, _, _, _) =>
            {
                captured = startInfo;
                calls++;
                return Task.FromResult(new ExecutorResult(0, false));
            });

        var result = await executor.ExecuteAsync(
            OperationKind.ServiceEnable, "apache2.service", 30,
            NoOutput, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, calls);
        Assert.NotNull(captured);
        Assert.Equal("sudo", captured.FileName);
        Assert.Equal(new[] { "-n", "/bin/systemctl", "enable", "--now", "apache2" }, captured.ArgumentList);
    }

    [Theory]
    [InlineData(OperationKind.ServiceStart, "start")]
    [InlineData(OperationKind.ServiceStop, "stop")]
    [InlineData(OperationKind.ServiceRestart, "restart")]
    [InlineData(OperationKind.ServiceStatus, "status")]
    public void BuildLinuxSystemctlArgv_ProducesSudoNonInteractiveArgv(OperationKind kind, string verb)
    {
        var argv = ServiceOperationExecutor.BuildLinuxSystemctlArgv(kind, "nginx");

        Assert.NotNull(argv);
        Assert.Equal(new[] { "-n", "/bin/systemctl", verb, "nginx" }, argv);
    }

    [Fact]
    public void BuildLinuxSystemctlArgv_StripsServiceSuffix()
    {
        var argv = ServiceOperationExecutor.BuildLinuxSystemctlArgv(OperationKind.ServiceStart, "apache2.service");

        Assert.NotNull(argv);
        Assert.Equal("apache2", argv[^1]);
    }

    [Fact]
    public void BuildLinuxSystemctlArgv_NonServiceKind_ReturnsNull()
        => Assert.Null(ServiceOperationExecutor.BuildLinuxSystemctlArgv(OperationKind.ServiceEnable, "nginx"));

    // --- ServiceGetLogs: journalctl argv (no shell interpolation) ---

    [Fact]
    public void BuildJournalctlArgv_NoFollow_ProducesArgv()
        => Assert.Equal(
            new[] { "-u", "nginx", "-n", "50", "--no-pager" },
            ServiceOperationExecutor.BuildJournalctlArgv("nginx", 50, follow: false));

    [Fact]
    public void BuildJournalctlArgv_Follow_AppendsFollowFlag()
        => Assert.Equal(
            new[] { "-u", "nginx", "-n", "100", "--no-pager", "-f" },
            ServiceOperationExecutor.BuildJournalctlArgv("nginx", 100, follow: true));

    [Fact]
    public void BuildJournalctlArgv_StripsServiceSuffix()
        => Assert.Equal("apache2", ServiceOperationExecutor.BuildJournalctlArgv("apache2.service", 10, false)[1]);

    [Fact]
    public async Task ExecuteAsync_GetLogs_InvalidServiceName_ReturnsFailureWithoutSpawning()
    {
        var messages = new List<string>();
        var result = await Build().ExecuteAsync(
            OperationKind.ServiceGetLogs, "bad;name", new Dictionary<string, string>(), 30,
            (m, _) => { messages.Add(m); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains(messages, m => m.Contains("Invalid service name", StringComparison.OrdinalIgnoreCase));
    }
}
