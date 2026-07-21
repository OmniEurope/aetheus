// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Tests.Pipelines;

// Audit F-ENG-02: a malformed container resource limit must fail before dispatch rather than silently
// leaving the container uncapped.
public class ContainerIsolationWarningTests
{
    [Theory]
    [InlineData(true, null, false, false, "no image")]
    [InlineData(true, "alpine:3.22", false, false, "no Docker")]
    [InlineData(false, null, true, false, "containers-only")]
    [InlineData(true, "alpine:3.22", false, true, null)]
    public void CheckIsolationPolicy_FailsClosedForUnsafeStageRunnerCombinations(
        bool container,
        string? image,
        bool requireContainer,
        bool dockerAvailable,
        string? expectedFragment)
    {
        var stage = new PipelineStageDefinition
        {
            Isolation = new PipelineIsolationDefinition
            {
                Mode = container ? PipelineIsolationDefinition.ModeContainer : PipelineIsolationDefinition.ModeProcess,
                Image = image
            }
        };
        var server = new Server
        {
            Name = "runner-1",
            DockerAvailable = dockerAvailable,
            RequireContainerIsolation = requireContainer
        };

        var result = PipelineRunHelpers.CheckIsolationPolicy("Build", stage, server);

        if (expectedFragment is null)
            Assert.Null(result);
        else
            Assert.Contains(expectedFragment, result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, null, false, false, "no image")]
    [InlineData(true, "alpine:3.22", false, false, "no Docker")]
    [InlineData(false, null, true, false, "containers-only")]
    [InlineData(true, "alpine:3.22", false, true, null)]
    public void CheckRunIsolationPolicy_FailsClosedForUnsafeRunnerCombinations(
        bool container,
        string? image,
        bool requireContainer,
        bool dockerAvailable,
        string? expectedFragment)
    {
        var isolation = new PipelineIsolationDefinition
        {
            Mode = container ? PipelineIsolationDefinition.ModeContainer : PipelineIsolationDefinition.ModeProcess,
            Image = image
        };
        var server = new Server
        {
            Name = "runner-1",
            DockerAvailable = dockerAvailable,
            RequireContainerIsolation = requireContainer
        };

        var result = PipelineRunHelpers.CheckRunIsolationPolicy(server, isolation);

        if (expectedFragment is null)
            Assert.Null(result);
        else
            Assert.Contains(expectedFragment, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyContainerIsolation_MalformedLimits_AreRejected()
    {
        var task = new ServerTask { Name = "s", Command = "echo" };
        var iso = new PipelineIsolationDefinition { Mode = "container", Image = "alpine", Memory = "not-a-limit", Cpus = "??" };

        var error = Assert.Throws<ArgumentException>(() => PipelineRunHelpers.ApplyContainerIsolation(task, iso));

        Assert.Null(task.ContainerMemory);
        Assert.Null(task.ContainerCpus);
        Assert.Contains("memory", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CPU", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyContainerIsolation_ValidLimits_AppliedWithoutWarnings()
    {
        var task = new ServerTask { Name = "s", Command = "echo" };
        var iso = new PipelineIsolationDefinition { Mode = "container", Image = "alpine", Memory = "512m", Cpus = "1.5" };

        var warnings = PipelineRunHelpers.ApplyContainerIsolation(task, iso);

        Assert.Equal("512m", task.ContainerMemory);
        Assert.Equal("1.5", task.ContainerCpus);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ApplyContainerIsolation_NotContainer_NoOpNoWarnings()
    {
        var task = new ServerTask { Name = "s", Command = "echo" };
        var warnings = PipelineRunHelpers.ApplyContainerIsolation(task, new PipelineIsolationDefinition { Mode = "host" });

        Assert.Empty(warnings);
        Assert.NotEqual(ExecutorType.Container, task.Executor);
    }
}
