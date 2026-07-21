// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Radzen;

namespace Aetheus.Front.Tests;

public class PipelineHelperTests
{
    [Theory]
    [InlineData(PipelineStatus.Success, BadgeStyle.Success)]
    [InlineData(PipelineStatus.Failed, BadgeStyle.Danger)]
    [InlineData(PipelineStatus.Running, BadgeStyle.Info)]
    [InlineData(PipelineStatus.Cancelled, BadgeStyle.Warning)]
    public void GetRunBadge_ReturnsCorrectStyle(PipelineStatus status, BadgeStyle expected)
    {
        Assert.Equal(expected, PipelineHelper.GetRunBadge(status));
    }

    [Fact]
    public void GetRunBadge_UnknownStatus_ReturnsLight()
    {
        Assert.Equal(BadgeStyle.Light, PipelineHelper.GetRunBadge((PipelineStatus)999));
    }

    [Theory]
    [InlineData(PipelineStatus.Running)]
    [InlineData(PipelineStatus.Pending)]
    [InlineData(PipelineStatus.WaitingForApproval)]
    public void GetCurrentStepLabel_ReturnsRunningOrNextPendingStep(PipelineStatus status)
    {
        var run = new PipelineRunDto
        {
            Status = status,
            Steps =
            [
                new PipelineStepRunDto { StageName = "Build", StepName = "Compile", Status = TaskExecutionStatus.Pending },
                new PipelineStepRunDto { StageName = "System:QA", StepName = "Tests", Status = TaskExecutionStatus.Running }
            ]
        };

        Assert.Equal("QA · Tests", PipelineHelper.GetCurrentStepLabel(run));
    }

    [Fact]
    public void GetCurrentStepLabel_ReturnsNullForTerminalRun()
    {
        var run = new PipelineRunDto
        {
            Status = PipelineStatus.Success,
            Steps = [new PipelineStepRunDto { StageName = "Build", StepName = "Compile", Status = TaskExecutionStatus.Success }]
        };

        Assert.Null(PipelineHelper.GetCurrentStepLabel(run));
    }

    [Fact]
    public void GetCurrentStepLabel_UsesAssignedStepBeforePendingStep()
    {
        var run = new PipelineRunDto
        {
            Status = PipelineStatus.Running,
            Steps =
            [
                new PipelineStepRunDto { StageName = "Build", StepName = "Pending", Status = TaskExecutionStatus.Pending },
                new PipelineStepRunDto { StageName = "QA", StepName = "Assigned", Status = TaskExecutionStatus.Assigned }
            ]
        };

        Assert.Equal("QA · Assigned", PipelineHelper.GetCurrentStepLabel(run));
    }
}
