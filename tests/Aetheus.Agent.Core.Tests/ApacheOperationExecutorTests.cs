// SPDX-License-Identifier: EUPL-1.2
using System.Text;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Operations;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Tests;

public class ApacheOperationExecutorTests
{
    private static ApacheOperationExecutor Build() =>
        new(Options.Create(new AetheusAgentOptions()), NullLogger<ApacheOperationExecutor>.Instance);

    private static Task NoOutput(string _, TaskLogLevel __) => Task.CompletedTask;

    [Theory]
    [InlineData(OperationKind.ApacheGetConfig, true)]
    [InlineData(OperationKind.ApacheGetHtaccess, true)]
    [InlineData(OperationKind.ApacheSaveHtaccess, true)]
    [InlineData(OperationKind.ApacheReload, true)]
    [InlineData(OperationKind.CertbotObtain, false)]
    [InlineData(OperationKind.PortsentryUnblock, false)]
    public void CanHandle_IncludesNewReadWriteKinds(OperationKind kind, bool expected)
        => Assert.Equal(expected, Build().CanHandle(kind));

    // Note: production dispatch (PollingService) always calls the env-aware overload, which is where the
    // typed read/write ops are routed - the tests use it too (an empty dict for the no-payload reads).
    private static readonly IReadOnlyDictionary<string, string> NoEnv = new Dictionary<string, string>();

    [Fact]
    public async Task GetHtaccess_InvalidDocumentRoot_ReturnsFailureWithoutTouchingFs()
    {
        var messages = new List<string>();
        var result = await Build().ExecuteAsync(
            OperationKind.ApacheGetHtaccess, "../../etc", NoEnv, 10,
            (m, _) => { messages.Add(m); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains(messages, m => m.Contains("Invalid document root", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetHtaccess_AbsentFile_ReturnsEmptySuccess()
    {
        // A valid unix-absolute path that does not exist: the read is an honest empty success, not a failure.
        var result = await Build().ExecuteAsync(
            OperationKind.ApacheGetHtaccess, "/nonexistent/aetheus-test-dir", NoEnv, 10,
            NoOutput, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task SaveHtaccess_MissingEnvPayload_ReturnsFailure()
    {
        var messages = new List<string>();
        var result = await Build().ExecuteAsync(
            OperationKind.ApacheSaveHtaccess, "/var/www/html",
            new Dictionary<string, string>(), 10,
            (m, _) => { messages.Add(m); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains(messages, m => m.Contains("AETHEUS_APACHE_HTACCESS_B64", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveHtaccess_InvalidBase64_ReturnsFailure()
    {
        var messages = new List<string>();
        var result = await Build().ExecuteAsync(
            OperationKind.ApacheSaveHtaccess, "/var/www/html",
            new Dictionary<string, string> { ["AETHEUS_APACHE_HTACCESS_B64"] = "not-base64!!!" }, 10,
            (m, _) => { messages.Add(m); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains(messages, m => m.Contains("base64", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SaveThenGetHtaccess_RoundTrips()
    {
        byte[]? storedContent = null;
        string? storedPath = null;
        var executor = new ApacheOperationExecutor(
            Options.Create(new AetheusAgentOptions()),
            NullLogger<ApacheOperationExecutor>.Instance)
        {
            HtaccessFileExists = path => path == storedPath && storedContent is not null,
            WriteHtaccessAsync = (path, bytes, _) =>
            {
                storedPath = path;
                storedContent = bytes.ToArray();
                return Task.CompletedTask;
            },
            ReadHtaccessAsync = (_, _) => Task.FromResult(Encoding.UTF8.GetString(storedContent!))
        };
        const string documentRoot = "/var/www/html";
        const string content = "RewriteEngine On\nRewriteRule ^old$ /new [R=301]\n";
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));

        var save = await executor.ExecuteAsync(
            OperationKind.ApacheSaveHtaccess, documentRoot,
            new Dictionary<string, string> { ["AETHEUS_APACHE_HTACCESS_B64"] = b64 }, 10,
            NoOutput, TestContext.Current.CancellationToken);

        Assert.Equal(0, save.ExitCode);
        Assert.EndsWith(".htaccess", storedPath, StringComparison.Ordinal);
        Assert.Equal(content, Encoding.UTF8.GetString(storedContent!));

        var read = new List<string>();
        var get = await executor.ExecuteAsync(
            OperationKind.ApacheGetHtaccess, documentRoot, NoEnv, 10,
            (message, _) =>
            {
                read.Add(message);
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(0, get.ExitCode);
        Assert.Contains(read, message => message.Contains("RewriteEngine On", StringComparison.Ordinal));
    }
}
