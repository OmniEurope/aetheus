// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Operations;
using Aetheus.Shared.Constants;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Tests;

public class MailOperationExecutorTests
{
    private static MailOperationExecutor Build() =>
        new(Options.Create(new AetheusAgentOptions()), NullLogger<MailOperationExecutor>.Instance);

    [Theory]
    [InlineData(OperationKind.MailSetup, true)]
    [InlineData(OperationKind.MailStartPostfix, true)]
    [InlineData(OperationKind.MailGetLogs, true)]
    [InlineData(OperationKind.MailChangePassword, true)]
    [InlineData(OperationKind.MailRemoveDomain, true)]
    [InlineData(OperationKind.MailDeleteAccount, true)]
    [InlineData(OperationKind.MailRemoveAlias, true)]
    [InlineData(OperationKind.MailDkimRead, true)]
    [InlineData(OperationKind.ServiceInstall, false)]
    [InlineData(OperationKind.CronSave, false)]
    public void CanHandle_OnlyMailKinds(OperationKind kind, bool expected)
        => Assert.Equal(expected, Build().CanHandle(kind));

    [Theory]
    [InlineData(OperationKind.MailRemoveDomain, "bad domain!")]      // invalid domain
    [InlineData(OperationKind.MailDeleteAccount, "not-an-email")]    // invalid email target
    [InlineData(OperationKind.MailRemoveAlias, "no-at-sign")]        // invalid alias email
    [InlineData(OperationKind.MailDkimRead, "bad selector!")]        // invalid DKIM selector
    public async Task ExecuteAsync_RemoveReadOps_InvalidTarget_ReturnsFailureWithoutSpawning(OperationKind kind, string target)
    {
        var messages = new List<string>();
        var result = await Build().ExecuteAsync(
            kind, target, new Dictionary<string, string>(), 30,
            (m, _) => { messages.Add(m); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains(messages, m => m.Contains("Invalid mail operation parameters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_MailDeleteAccount_MissingDomain_ReturnsFailureWithoutSpawning()
    {
        // A valid email but no AETHEUS_MAIL_DOMAIN in env must be rejected before spawning.
        var messages = new List<string>();
        var result = await Build().ExecuteAsync(
            OperationKind.MailDeleteAccount, "user@example.com", new Dictionary<string, string>(), 30,
            (m, _) => { messages.Add(m); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains(messages, m => m.Contains("Invalid mail operation parameters", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("not an email", "SecureP@ss123!")] // bad target email
    [InlineData("user@example.com", "short")]       // password too short
    public async Task ExecuteAsync_MailChangePassword_InvalidParams_ReturnsFailureWithoutSpawning(string email, string password)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal) { [MailSetupEnv.AccountPassword] = password };
        var messages = new List<string>();
        var result = await Build().ExecuteAsync(
            OperationKind.MailChangePassword, email, env, 30,
            (m, _) => { messages.Add(m); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains(messages, m => m.Contains("Invalid mail operation parameters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildSetupArgv_ProducesExactArgv_PasswordAbsent()
        => Assert.Equal(
            new[] { "-n", "/usr/local/lib/aetheus/mail-setup", "mail.example.com", "example.com", "default", "admin@example.com", "1024" },
            MailOperationExecutor.BuildSetupArgv("mail.example.com", "example.com", "default", "admin@example.com", 1024));

    private static Dictionary<string, string> ValidEnv() => new(StringComparer.Ordinal)
    {
        [MailSetupEnv.Hostname] = "mail.example.com",
        [MailSetupEnv.DkimSelector] = "default",
        [MailSetupEnv.AdminEmail] = "admin@example.com",
        [MailSetupEnv.QuotaMb] = "1024",
        [MailSetupEnv.AdminPassword] = "SecureP@ss123!"
    };

    [Fact]
    public async Task ExecuteAsync_MailSetup_InvalidDomain_ReturnsFailureWithoutSpawning()
    {
        var messages = new List<string>();
        var result = await Build().ExecuteAsync(
            OperationKind.MailSetup, "not a domain", ValidEnv(), 60,
            (m, _) => { messages.Add(m); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains(messages, m => m.Contains("Invalid mail setup parameters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_MailSetup_MissingPassword_ReturnsFailureWithoutSpawning()
    {
        var env = ValidEnv();
        env.Remove(MailSetupEnv.AdminPassword);
        var messages = new List<string>();
        var result = await Build().ExecuteAsync(
            OperationKind.MailSetup, "example.com", env, 60,
            (m, _) => { messages.Add(m); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains(messages, m => m.Contains("Invalid mail setup parameters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_MailSetup_NonIntegerQuota_ReturnsFailureWithoutSpawning()
    {
        var env = ValidEnv();
        env[MailSetupEnv.QuotaMb] = "not-a-number";
        var messages = new List<string>();
        var result = await Build().ExecuteAsync(
            OperationKind.MailSetup, "example.com", env, 60,
            (m, _) => { messages.Add(m); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains(messages, m => m.Contains("Invalid mail setup parameters", StringComparison.OrdinalIgnoreCase));
    }
}
