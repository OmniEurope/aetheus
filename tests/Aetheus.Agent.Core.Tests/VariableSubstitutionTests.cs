// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Agent.Core.Operations;
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public class VariableSubstitutionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PipelineArtifactOperationExecutor _executor;

    public VariableSubstitutionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aetheus-sub-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var apiClient = Substitute.For<IServerApiClient>();
        _executor = new PipelineArtifactOperationExecutor(
            apiClient,
            NullLogger<PipelineArtifactOperationExecutor>.Instance,
            TimeProvider.System);
    }

    [Fact]
    public async Task SubstituteVariables_ReplacesTokensInFile()
    {
        // Arrange - create a config file with #{VAR}# tokens
        var configPath = Path.Combine(_tempDir, "appsettings.json");
        await File.WriteAllTextAsync(configPath, """
            {
                "Version": "#{APP_VERSION}#",
                "Environment": "#{ENVIRONMENT}#",
                "BuildId": "#{BUILD_BUILDID}#"
            }
            """, TestContext.Current.CancellationToken);

        var envVars = new Dictionary<string, string>
        {
            ["AETHEUS_WORKING_DIR"] = _tempDir,
            ["APP_VERSION"] = "2.1.0",
            ["ENVIRONMENT"] = "production",
            ["BUILD_BUILDID"] = "42"
        };

        var target = JsonSerializer.Serialize(new List<string> { "appsettings.json" });
        Func<string, TaskLogLevel, Task> onOutput = (_, _) => Task.CompletedTask;

        // Act
        var result = await _executor.ExecuteAsync(
            OperationKind.PipelineSubstituteVariables,
            target, envVars, 300, onOutput, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, result.ExitCode);
        var content = await File.ReadAllTextAsync(configPath, TestContext.Current.CancellationToken);
        Assert.Contains("\"2.1.0\"", content);
        Assert.Contains("\"production\"", content);
        Assert.Contains("\"42\"", content);
        Assert.DoesNotContain("#{APP_VERSION}#", content);
        Assert.DoesNotContain("#{ENVIRONMENT}#", content);
    }

    [Fact]
    public async Task SubstituteVariables_LeavesUnknownTokensUntouched()
    {
        var configPath = Path.Combine(_tempDir, "config.txt");
        await File.WriteAllTextAsync(configPath, "key=#{KNOWN}# other=#{UNKNOWN}#", TestContext.Current.CancellationToken);

        var envVars = new Dictionary<string, string>
        {
            ["AETHEUS_WORKING_DIR"] = _tempDir,
            ["KNOWN"] = "resolved"
        };

        var target = JsonSerializer.Serialize(new List<string> { "config.txt" });
        Func<string, TaskLogLevel, Task> onOutput = (_, _) => Task.CompletedTask;
        await _executor.ExecuteAsync(
            OperationKind.PipelineSubstituteVariables,
            target, envVars, 300, onOutput, TestContext.Current.CancellationToken);

        var content = await File.ReadAllTextAsync(configPath, TestContext.Current.CancellationToken);
        Assert.Contains("key=resolved", content);
        Assert.Contains("#{UNKNOWN}#", content);
    }

    [Fact]
    public async Task SubstituteVariables_NoTargetFiles_FailsHonestly()
    {
        var envVars = new Dictionary<string, string>
        {
            ["AETHEUS_WORKING_DIR"] = _tempDir
        };

        Func<string, TaskLogLevel, Task> onOutput = (_, _) => Task.CompletedTask;
        var result = await _executor.ExecuteAsync(
            OperationKind.PipelineSubstituteVariables,
            "[]", envVars, 300, onOutput, TestContext.Current.CancellationToken);

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task SubstituteVariables_NoMatchingFiles_FailsHonestly()
    {
        var envVars = new Dictionary<string, string>
        {
            ["AETHEUS_WORKING_DIR"] = _tempDir
        };

        var result = await _executor.ExecuteAsync(
            OperationKind.PipelineSubstituteVariables,
            "[\"missing-*.json\"]", envVars, 300,
            (_, _) => Task.CompletedTask,
            TestContext.Current.CancellationToken);

        Assert.NotEqual(0, result.ExitCode);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* best-effort cleanup */ }
    }
}
