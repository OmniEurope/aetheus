// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// Covers the pre-flight validation branches of <see cref="ContainerRunExecutor"/> that return
/// before any <c>docker</c> process is spawned - so they are exercisable without a Docker daemon.
/// </summary>
public class ContainerRunExecutorTests
{
    private static ContainerRunExecutor NewExecutor() =>
        new(Options.Create(new AetheusAgentOptions()), NullLogger<ContainerRunExecutor>.Instance);

    private static async Task<(ExecutorResult Result, List<string> Errors)> RunAsync(ContainerSpec spec)
    {
        var errors = new List<string>();
        Task Sink(string line, TaskLogLevel level)
        {
            if (level == TaskLogLevel.Error) errors.Add(line);
            return Task.CompletedTask;
        }

        var result = await NewExecutor().ExecuteAsync(spec, "echo hi", new Dictionary<string, string>(), 60, Sink, TestContext.Current.CancellationToken);
        return (result, errors);
    }

    [Fact]
    public async Task ExecuteAsync_NoImage_BlocksWithoutSpawningDocker()
    {
        var (result, errors) = await RunAsync(new ContainerSpec { Image = "", WorkspaceKey = 1 });

        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains(errors, e => e.Contains("no image", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("evil")]
    [InlineData("docker")]
    [InlineData("../runsc")]
    public async Task ExecuteAsync_DisallowedRuntime_Blocks(string runtime)
    {
        var (result, errors) = await RunAsync(new ContainerSpec { Image = "alpine", Runtime = runtime, WorkspaceKey = 1 });

        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains(errors, e => e.Contains("not allowed", StringComparison.OrdinalIgnoreCase));
    }

    // --- S-TECH-Q9MF: multi-line secrets via a mounted file, never on argv ---

    [Theory]
    [InlineData("PEM_KEY", true)]
    [InlineData("_x", true)]
    [InlineData("A1_B2", true)]
    [InlineData("1BAD", false)]     // leading digit
    [InlineData("has space", false)]
    [InlineData("has=eq", false)]
    [InlineData("", false)]
    public void IsValidShellKey_AcceptsOnlyPosixIdentifiers(string key, bool expected) =>
        Assert.Equal(expected, ContainerRunExecutor.IsValidShellKey(key));

    [Fact]
    public async Task WriteSecretsFileLines_EmitsSingleQuoteEscapedExports()
    {
        var path = Path.Combine(Path.GetTempPath(), $"secrets-test-{Guid.NewGuid():N}.sh");
        try
        {
            var secrets = new List<KeyValuePair<string, string>>
            {
                new("PEM", "-----BEGIN-----\nline1\nline2\n-----END-----"),
                new("QUOTED", "a'b"),
            };
            await ContainerRunExecutor.WriteSecretsFileLinesAsync(secrets, path, TestContext.Current.CancellationToken);

            var content = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            // Multi-line value survives verbatim inside the single-quoted export (newlines preserved).
            Assert.Contains("export PEM='-----BEGIN-----\nline1\nline2\n-----END-----'", content, StringComparison.Ordinal);
            // A single quote in the value is escaped as '\'' so the export stays well-formed.
            Assert.Contains("export QUOTED='a'\\''b'", content, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BuildDockerRun_MultiLineSecrets_UseMountAndSource_NeverArgv()
    {
        var exec = NewExecutor();
        var method = typeof(ContainerRunExecutor).GetMethod("BuildDockerRun",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var psi = (System.Diagnostics.ProcessStartInfo)method.Invoke(exec,
            [new ContainerSpec { Image = "alpine", WorkspaceKey = 1 }, "/host/ws", "step.sh", "prom-x", null, "/tmp/prom-x.secrets.sh"])!;
        var args = psi.ArgumentList;

        // No inline -e: a secret value can never land on the world-readable /proc/<pid>/cmdline.
        Assert.DoesNotContain("-e", args);
        // The secrets file is bind-mounted read-only and sourced before the step script runs.
        Assert.Contains(args, a => a.Contains(".secrets.sh:", StringComparison.Ordinal) && a.EndsWith(":ro", StringComparison.Ordinal));
        Assert.Contains(args, a => a.StartsWith(". /run/aetheus/", StringComparison.Ordinal) && a.Contains("exec bash /w/step.sh", StringComparison.Ordinal));
    }
}
