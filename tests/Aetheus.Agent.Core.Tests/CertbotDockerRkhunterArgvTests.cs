// SPDX-License-Identifier: EUPL-1.2
using System.Runtime.InteropServices;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Operations;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// Argv-construction coverage for three operation executors that were previously untested
/// (Certbot / Docker / Rkhunter). The exact argv IS the security boundary (argv-exact sudoers /
/// docker CLI, no shell), so a drift must break a test, not a production deploy.
/// </summary>
public class CertbotDockerRkhunterArgvTests
{
    // ── Certbot ─────────────────────────────────────────────────────────

    [Fact]
    public void Certbot_Obtain_Argv_IsExact()
    {
        var argv = CertbotOperationExecutor.BuildObtainArgv("example.com");
        Assert.Equal(new[] { "-n", "/usr/local/lib/aetheus/aetheus-certbot-issue", "example.com" }, argv);
    }

    [Theory]
    [InlineData(OperationKind.CertbotRenew, "renew", "example.com")]
    [InlineData(OperationKind.CertbotDelete, "delete", "example.com")]
    [InlineData(OperationKind.CertbotRevoke, "revoke", "example.com")]
    public void Certbot_Manage_Argv_IncludesLineageName(OperationKind kind, string verb, string certName)
    {
        var argv = CertbotOperationExecutor.BuildManageArgv(kind, verb, certName);
        Assert.Equal(new[] { "-n", "/usr/local/lib/aetheus/aetheus-certbot-manage", verb, certName }, argv);
    }

    [Fact]
    public void Certbot_RenewAll_Argv_OmitsLineageName()
    {
        var argv = CertbotOperationExecutor.BuildManageArgv(OperationKind.CertbotRenewAll, "renew-all", "ignored");
        Assert.Equal(new[] { "-n", "/usr/local/lib/aetheus/aetheus-certbot-manage", "renew-all" }, argv);
    }

    [Fact]
    public async Task Certbot_ExecuteAsync_OutsideLinux_FailsClosedForObtainAndManage()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.True(new CertbotOperationExecutor(
                Options.Create(new AetheusAgentOptions()), NullLogger<CertbotOperationExecutor>.Instance)
                .CanHandle(OperationKind.CertbotObtain));
            return;
        }

        var sut = new CertbotOperationExecutor(
            Options.Create(new AetheusAgentOptions()), NullLogger<CertbotOperationExecutor>.Instance);
        var output = new List<(string Message, TaskLogLevel Level)>();
        Task Capture(string message, TaskLogLevel level)
        {
            output.Add((message, level));
            return Task.CompletedTask;
        }

        var obtain = await sut.ExecuteAsync(
            OperationKind.CertbotObtain, "example.com", new Dictionary<string, string>(),
            30, Capture, TestContext.Current.CancellationToken);
        var renew = await sut.ExecuteAsync(
            OperationKind.CertbotRenew, "example.com", 30, Capture, TestContext.Current.CancellationToken);

        Assert.Equal(-1, obtain.ExitCode);
        Assert.Equal(-1, renew.ExitCode);
        Assert.All(output, entry =>
        {
            Assert.Equal(TaskLogLevel.Error, entry.Level);
            Assert.Contains("only supported on Linux", entry.Message, StringComparison.Ordinal);
        });
    }

    // ── Docker ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OperationKind.DockerRestartContainer, "restart")]
    [InlineData(OperationKind.DockerStartContainer, "start")]
    [InlineData(OperationKind.DockerStopContainer, "stop")]
    [InlineData(OperationKind.DockerPullImage, "pull")]
    public void Docker_Argv_IsVerbThenTarget(OperationKind kind, string verb)
    {
        var argv = DockerOperationExecutor.BuildArgs(kind, "my-container");
        Assert.Equal(new[] { verb, "my-container" }, argv);
    }

    [Fact]
    public void Docker_Argv_UnhandledKind_IsNull()
        => Assert.Null(DockerOperationExecutor.BuildArgs(OperationKind.CronSave, "x"));

    // ── Rkhunter ────────────────────────────────────────────────────────

    [Fact]
    public void Rkhunter_Scan_Argv_IsExact()
    {
        var argv = RkhunterOperationExecutor.BuildSudoArgv(OperationKind.RkhunterScan);
        Assert.Equal(
            new[] { "-n", "/usr/bin/rkhunter", "--check", "--skip-keypress", "--nocolors", "--report-warnings-only" },
            argv);
    }

    [Fact]
    public void Rkhunter_Update_Argv_IsExact()
    {
        var argv = RkhunterOperationExecutor.BuildSudoArgv(OperationKind.RkhunterUpdate);
        Assert.Equal(new[] { "-n", "/usr/bin/rkhunter", "--update", "--nocolors" }, argv);
    }

    [Fact]
    public void Rkhunter_Propupd_Argv_IsExact()
    {
        var argv = RkhunterOperationExecutor.BuildSudoArgv(OperationKind.RkhunterPropupd);
        Assert.Equal(new[] { "-n", "/usr/bin/rkhunter", "--propupd", "--nocolors" }, argv);
    }

    [Fact]
    public void Rkhunter_UnhandledKind_IsNull()
        => Assert.Null(RkhunterOperationExecutor.BuildSudoArgv(OperationKind.CronSave));
}
