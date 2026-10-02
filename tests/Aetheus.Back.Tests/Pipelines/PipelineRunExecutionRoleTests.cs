// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;

namespace Aetheus.Back.Tests;

public class PipelineRunExecutionRoleTests
{
    [Theory]
    [InlineData("build")]
    [InlineData("BUILD")]
    [InlineData("deploy")]
    public void ValidateExecutionRoles_AcceptsSupportedRole(string role)
    {
        var stages = new[]
        {
            new PipelineStageDefinition { Name = "stage", ExecutionRole = role }
        };

        Assert.Empty(PipelineRunHelpers.ValidateExecutionRoles(stages));
    }

    [Fact]
    public void ValidateExecutionRoles_RejectsUnknownRole()
    {
        var stages = new[]
        {
            new PipelineStageDefinition { Name = "stage", ExecutionRole = "production" }
        };

        var error = Assert.Single(PipelineRunHelpers.ValidateExecutionRoles(stages));
        Assert.Contains("execution_role must be 'build' or 'deploy'", error, StringComparison.Ordinal);
    }
}
