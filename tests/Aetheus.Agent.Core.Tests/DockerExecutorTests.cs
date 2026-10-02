// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;
using Aetheus.Agent.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public class DockerExecutorTests
{
    private readonly ILogger<DockerExecutor> _loggerMock = Substitute.For<ILogger<DockerExecutor>>();
    private readonly ICommandValidator _validatorMock = Substitute.For<ICommandValidator>();
    private readonly DockerExecutor _sut;

    public DockerExecutorTests()
    {
        _validatorMock.IsAllowed(Arg.Any<string>()).Returns(true);
        _validatorMock.GetDangerousEnvironmentVariableNames(Arg.Any<Dictionary<string, string>>())
            .Returns([]);
        var options = Options.Create(new AetheusAgentOptions());
        var processRunner = new ExecutorProcessRunner(
            new AgentRuntimeHealth(TimeProvider.System),
            NullLogger<ExecutorProcessRunner>.Instance);
        _sut = new DockerExecutor(_validatorMock, options, processRunner, _loggerMock);
    }

    [Fact]
    public void Type_ReturnsDocker()
    {
        Assert.Equal(ExecutorType.Docker, _sut.Type);
    }

    [Fact]
    public async Task ExecuteAsync_NoSpace_ReturnsError()
    {
        var outputMessages = new List<string>();

        var result = await _sut.ExecuteAsync(
            "invalid-no-space",
            new Dictionary<string, string>(),
            30,
            (msg, level) => { outputMessages.Add(msg); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains(outputMessages, m => m.Contains("Invalid docker exec command"));
    }

    [Fact]
    public void TryParseExecCommand_WithSpace_ParsesContainerAndCommand()
    {
        // Assert the real parsing contract on the extracted pure method, without depending on a docker
        // binary being present/absent (the old test spawned a process and only checked a Debug log call).
        var ok = DockerExecutor.TryParseExecCommand("test-container echo hello", out var container, out var inner, out var error);

        Assert.True(ok);
        Assert.Equal("test-container", container);
        Assert.Equal("echo hello", inner);
        Assert.Empty(error);
    }

    [Fact]
    public void TryParseExecCommand_NoSpace_ReturnsError()
    {
        var ok = DockerExecutor.TryParseExecCommand("invalid-no-space", out var container, out _, out var error);

        Assert.False(ok);
        Assert.Empty(container); // empty id distinguishes the missing-space case from a malformed id
        Assert.Contains("missing container ID", error);
    }

    [Fact]
    public void TryParseExecCommand_MalformedContainerId_ReturnsError()
    {
        var ok = DockerExecutor.TryParseExecCommand("bad;id echo hi", out var container, out _, out var error);

        Assert.False(ok);
        Assert.Equal("bad;id", container); // split succeeded, but the id fails the format check
        Assert.Contains("Invalid container ID format", error);
    }

    [Fact]
    public void BuildDockerExec_ContainsNoEnvironmentValueOrCommandInArgv()
    {
        const string secret = "top-secret-value";
        const string command = "printf done";

        var startInfo = DockerExecutor.BuildDockerExec("container-1");
        var arguments = startInfo.ArgumentList.ToArray();

        Assert.Equal(["exec", "-i", "container-1", "/bin/sh"], arguments);
        Assert.DoesNotContain(arguments, argument => argument.Contains(secret, StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, argument => argument.Contains(command, StringComparison.Ordinal));
        Assert.True(startInfo.RedirectStandardInput);
    }

    [Fact]
    public void BuildStandardInputScript_QuotesSecretsAndPreservesCommand()
    {
        var script = DockerExecutor.BuildStandardInputScript(
            new Dictionary<string, string>
            {
                ["TOKEN"] = "line 1\nline '2'",
                ["EMPTY"] = string.Empty
            },
            "printf done");

        Assert.Equal(
            "export TOKEN='line 1\nline '\\''2'\\'''\nexport EMPTY=''\nprintf done\n",
            script);
    }

    [Theory]
    [InlineData("VALID_NAME_42", true)]
    [InlineData("_VALID", true)]
    [InlineData("9INVALID", false)]
    [InlineData("INVALID-NAME", false)]
    [InlineData("INVALID\nNAME", false)]
    public void IsValidShellKey_RequiresPosixIdentifier(string key, bool expected) =>
        Assert.Equal(expected, DockerExecutor.IsValidShellKey(key));
}
