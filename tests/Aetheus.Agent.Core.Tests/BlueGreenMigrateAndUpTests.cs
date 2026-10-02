// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;
using Aetheus.Agent.Core.Operations;
using Aetheus.Agent.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// A360-42. <c>BlueGreenOperationExecutor</c> sat at 40,9 % line coverage - the least covered file on
/// the most dangerous path in the product. The existing suite covered rollback and the switch's
/// failure branch; migrate and up, which run BEFORE any traffic moves, had none.
///
/// These pin the refusals, because on this path a refusal is the feature: both colours share one
/// schema and the previous colour is still answering requests while the new one migrates, so a gate
/// that cannot see its inputs must stop rather than wave the bundle through.
/// </summary>
public sealed class BlueGreenMigrateAndUpTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"bg-migrate-{Guid.NewGuid():N}");

    public BlueGreenMigrateAndUpTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Migrate_WithoutMigrationSources_RefusesInsteadOfRunningTheBundleUngated()
    {
        var env = BaseEnvironment();
        var log = new List<string>();

        var result = await Execute(OperationKind.BlueGreenMigrate, env, log);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(log, line => line.Contains("AETHEUS_BG_MIGRATIONS_DIR is required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Migrate_WithAMigrationsPathThatDoesNotExist_Refuses()
    {
        // The dangerous shape: an unreadable directory yields no discovered migrations, which reads as
        // "nothing pending" and would disable the gate silently.
        var env = BaseEnvironment();
        env["AETHEUS_BG_MIGRATIONS_DIR"] = Path.Combine(_root, "nowhere");
        var log = new List<string>();

        var result = await Execute(OperationKind.BlueGreenMigrate, env, log);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(log, line => line.Contains("cannot inspect what is about to run", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Migrate_WhenTheSharedDatabaseDoesNotComeUp_StopsBeforeTheGate()
    {
        // Nothing downstream is meaningful without the database, and the message must name that cause
        // rather than surface as a confusing gate failure.
        var env = BaseEnvironment();
        env["AETHEUS_BG_MIGRATIONS_DIR"] = _root;
        var log = new List<string>();

        var result = await Execute(OperationKind.BlueGreenMigrate, env, log, new FailingComposeShell());

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(log, line => line.Contains("Shared database did not come up", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Up_OnAnEnvironmentItCannotValidate_IsRefusedBeforeDockerIsContacted()
    {
        // The scope check runs first on every operation; a target Compose would reject must never
        // reach a Docker invocation.
        var shell = new RecordingShell();
        var log = new List<string>();

        var result = await Execute(OperationKind.BlueGreenUp, BaseEnvironment(), log, shell, project: "../escape");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(shell.Commands);
    }

    [Fact]
    public async Task Up_RemovesTheIdleColourBeforeStartingIt_SoALeftoverContainerCannotHoldItsPort()
    {
        // A container left behind by an earlier failed deployment keeps its published ports, and
        // Compose cannot bind them again: production hit "port is already allocated" and every later
        // deployment failed the same way. `stop` is not enough - the container has to go.
        var shell = new PassingShell();

        await Execute(OperationKind.BlueGreenUp, BaseEnvironment(), [], shell);

        var rm = shell.Commands.FindIndex(c => c.Contains(" rm ", StringComparison.Ordinal));
        var up = shell.Commands.FindIndex(c => c.Contains(" up ", StringComparison.Ordinal));

        Assert.True(rm >= 0, "the idle colour is never removed before it is started");
        Assert.True(up >= 0, "the idle colour is never started");
        Assert.True(rm < up, "the removal must precede the start, or the port is still held");

        // Only the idle colour's own services: naming the profile alone would also take down the one
        // database both colours share, and the live colour must keep serving throughout.
        var removal = shell.Commands[rm];
        Assert.Contains("back-blue", removal, StringComparison.Ordinal);
        Assert.Contains("front-blue", removal, StringComparison.Ordinal);
        Assert.DoesNotContain("green", removal, StringComparison.Ordinal);
        Assert.DoesNotContain("database", removal, StringComparison.Ordinal);

        // No volume flag, ever: the containers are disposable, the data behind them is not.
        Assert.DoesNotContain("-v", removal, StringComparison.Ordinal);
        Assert.DoesNotContain("--volumes", removal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryBlueGreenOperation_RefusesAnInvalidScope()
    {
        // One refusal per operation, so a new operation cannot quietly skip the scope check.
        foreach (var operation in new[]
                 {
                     OperationKind.BlueGreenMigrate, OperationKind.BlueGreenUp, OperationKind.BlueGreenSwitch,
                     OperationKind.BlueGreenCommit, OperationKind.BlueGreenRollback
                 })
        {
            var shell = new RecordingShell();
            var result = await Execute(operation, BaseEnvironment(), [], shell, project: "has space");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Empty(shell.Commands);
        }
    }

    private Task<ExecutorResult> Execute(
        OperationKind operation,
        Dictionary<string, string> env,
        List<string> log,
        IShellRunner? shell = null,
        string project = "aetheus-demo")
        => CreateExecutor(shell ?? new PassingShell()).ExecuteAsync(
            operation, project, env, 60,
            (line, _) => { log.Add(line); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

    private Dictionary<string, string> BaseEnvironment()
    {
        var envFile = Path.Combine(_root, "stack.env");
        var composeFile = Path.Combine(_root, "compose.yml");
        if (!File.Exists(envFile))
            File.WriteAllText(envFile, "APPNAME=aetheus\nENV=demo\nDB_USER=aetheus\nDB_NAME=aetheus_demo\n");
        if (!File.Exists(composeFile)) File.WriteAllText(composeFile, "services: {}\n");
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AETHEUS_BG_STATE_DIR"] = _root,
            ["AETHEUS_BG_ENV_FILE"] = envFile,
            ["AETHEUS_BG_COMPOSE_FILES"] = composeFile,
            ["AETHEUS_BG_PORT_FRONT_BLUE"] = "10029",
            ["AETHEUS_BG_PORT_BACK_BLUE"] = "10030",
            ["AETHEUS_BG_PORT_FRONT_GREEN"] = "10031",
            ["AETHEUS_BG_PORT_BACK_GREEN"] = "10032"
        };
    }

    private static BlueGreenOperationExecutor CreateExecutor(IShellRunner shell) =>
        new(shell, Options.Create(new AetheusAgentOptions()), HttpFactory(),
            NullLogger<BlueGreenOperationExecutor>.Instance);

    private static IHttpClientFactory HttpFactory()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>())
            .Returns(_ => new HttpClient(new FixedStatusHandler(HttpStatusCode.ServiceUnavailable)));
        return factory;
    }

    private sealed class FixedStatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    /// <summary>Answers every shell call successfully, so the tests exercise the executor's decisions.</summary>
    private class PassingShell : IShellRunner
    {
        /// <summary>Docker invocations seen, in call order - used to assert nothing was contacted.</summary>
        internal List<string> Commands { get; } = [];

        [Obsolete("Matches the interface; the executor never uses the shell form.")]
        public Task<string> RunAsync(string command, CancellationToken ct, TimeSpan? timeout = null) =>
            Task.FromResult(string.Empty);

        public Task<string> RunWithStdinAsync(
            string fileName, IReadOnlyList<string> args, string stdin, CancellationToken ct, TimeSpan? timeout = null) =>
            Task.FromResult(string.Empty);

        public virtual Task<ShellExecResult> RunExecAsync(
            string fileName, IReadOnlyList<string> args, CancellationToken ct, TimeSpan? timeout = null)
        {
            Commands.Add($"{fileName} {string.Join(' ', args)}");
            // The Docker capability probe must succeed, or every test would stop on "no Docker"
            // instead of on the decision it is about.
            if (args.Count > 0 && args[0] == "info")
                return Task.FromResult(new ShellExecResult(0, "27.0.0", string.Empty));
            return Task.FromResult(new ShellExecResult(0, string.Empty, string.Empty));
        }

        public Task<ShellExecResult> RunExecAsync(
            string fileName, IReadOnlyList<string> args, IReadOnlyDictionary<string, string> environmentVariables,
            string workingDirectory, bool inheritEnvironment, int maxCapturedOutputBytes,
            CancellationToken ct, TimeSpan? timeout = null) => RunExecAsync(fileName, args, ct, timeout);
    }

    /// <summary>Records without answering: used to assert that nothing was contacted at all.</summary>
    private sealed class RecordingShell : PassingShell;

    /// <summary>Fails the Compose invocation, leaving the capability probe successful.</summary>
    private sealed class FailingComposeShell : PassingShell
    {
        public override Task<ShellExecResult> RunExecAsync(
            string fileName, IReadOnlyList<string> args, CancellationToken ct, TimeSpan? timeout = null)
        {
            Commands.Add($"{fileName} {string.Join(' ', args)}");
            if (args.Count > 0 && args[0] == "info")
                return Task.FromResult(new ShellExecResult(0, "27.0.0", string.Empty));
            return args.Contains("compose")
                ? Task.FromResult(new ShellExecResult(1, string.Empty, "database container failed to start"))
                : Task.FromResult(new ShellExecResult(0, string.Empty, string.Empty));
        }
    }
}
