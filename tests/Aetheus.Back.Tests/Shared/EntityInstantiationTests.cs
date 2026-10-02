// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Tests;

public class EntityInstantiationTests
{
    [Fact]
    public void ExternalLogin_Properties()
    {
        var entity = new ExternalLogin
        {
            Id = 1,
            UserId = 2,
            Provider = "GitHub",
            ProviderSubjectId = "sub123",
            DisplayName = "User"
        };
        Assert.Equal("GitHub", entity.Provider);
        Assert.Equal("sub123", entity.ProviderSubjectId);
    }

    [Fact]
    public void EnvironmentCheck_Properties()
    {
        var entity = new EnvironmentCheck
        {
            Id = 1,
            EnvironmentId = 2,
            Name = "Health",
            Type = EnvironmentCheckType.RestCallback,
            Configuration = "{}",
            IsRequired = true,
            TimeoutSeconds = 300
        };
        Assert.True(entity.IsRequired);
        Assert.Equal(300, entity.TimeoutSeconds);
    }

    [Fact]
    public void EnvironmentCheck_DefaultValues()
    {
        var entity = new EnvironmentCheck();
        Assert.True(entity.IsRequired);
        Assert.Equal(300, entity.TimeoutSeconds);
    }

    [Fact]
    public void PipelineArtifact_Properties()
    {
        var entity = new PipelineArtifact
        {
            Id = 1,
            PipelineRunId = 2,
            Name = "build.zip",
            FilePath = "/artifacts/build.zip",
            SizeBytes = 1024,
            StageName = "build",
            StepName = "package"
        };
        Assert.Equal(1024, entity.SizeBytes);
    }

    [Fact]
    public void PipelineApproval_Properties()
    {
        var entity = new PipelineApproval
        {
            Id = 1,
            PipelineRunId = 2,
            StageName = "deploy",
            EnvironmentId = 3,
            Status = ApprovalStatus.Pending,
            Comments = "Please approve"
        };
        Assert.Equal(ApprovalStatus.Pending, entity.Status);
    }

    [Fact]
    public void TestResult_Properties()
    {
        var entity = new TestResult
        {
            Id = 1,
            PipelineRunId = 2,
            StageName = "test",
            StepName = "unit",
            TestName = "MyTest",
            TestSuite = "Suite1",
            Outcome = TestOutcome.Passed,
            DurationMs = 123.4,
            ErrorMessage = null,
            StackTrace = null
        };
        Assert.Equal(TestOutcome.Passed, entity.Outcome);
        Assert.Equal(123.4, entity.DurationMs);
    }

    [Fact]
    public void PipelineTemplate_Properties()
    {
        var entity = new PipelineTemplate
        {
            Id = 1,
            Name = "CI",
            Description = "desc",
            Category = "Build",
            YamlContent = "stages:",
            Version = 2,
            Changelog = "v2"
        };
        Assert.Equal(2, entity.Version);
    }

    [Fact]
    public void PipelineTemplate_DefaultVersion()
    {
        var entity = new PipelineTemplate();
        Assert.Equal(1, entity.Version);
    }

    [Fact]
    public void AgentPoolServer_Properties()
    {
        var entity = new AgentPoolServer { AgentPoolId = 1, ServerId = 2 };
        Assert.Equal(1, entity.AgentPoolId);
        Assert.Equal(2, entity.ServerId);
    }

    [Fact]
    public void EnvironmentServer_Properties()
    {
        var entity = new EnvironmentServer { EnvironmentId = 1, ServerId = 2 };
        Assert.Equal(1, entity.EnvironmentId);
        Assert.Equal(2, entity.ServerId);
    }

    [Fact]
    public void PipelineStepRun_DefaultStatus()
    {
        var entity = new PipelineStepRun();
        Assert.Equal(TaskExecutionStatus.Pending, entity.Status);
    }

    [Fact]
    public void PipelineStepRun_AllProperties()
    {
        var entity = new PipelineStepRun
        {
            Id = 1,
            PipelineRunId = 2,
            StageName = "build",
            StepName = "compile",
            Order = 1,
            Status = TaskExecutionStatus.Success,
            ServerId = 3,
            TaskId = 4,
            ExitCode = 0,
            OutputVariablesJson = "{}",
            RetryCount = 1,
            ContinueOnError = true,
            MatrixLeg = "linux"
        };
        Assert.True(entity.ContinueOnError);
        Assert.Equal("linux", entity.MatrixLeg);
    }

    [Fact]
    public void PipelineRun_DefaultStatus()
    {
        var entity = new PipelineRun();
        Assert.Equal(PipelineStatus.Pending, entity.Status);
        Assert.Equal("{}", entity.AdditionalVariablesJson);
    }
}
