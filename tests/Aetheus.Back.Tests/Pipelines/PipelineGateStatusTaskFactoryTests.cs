// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data.Entities;
using NSubstitute;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// What the control plane refuses before a gate ever runs. Both refusals below describe a gate that
/// would look perfectly healthy in the run's history while checking nothing at all.
/// </summary>
public sealed class PipelineGateStatusTaskFactoryTests
{
    private readonly IPipelineStepTaskBuilder _taskBuilder = Substitute.For<IPipelineStepTaskBuilder>();

    private static PipelineStepDefinition Step() => new()
    {
        Name = "Blocking test gate",
        Type = "gate-status",
        StatusVariables = ["BACK_TEST_STATUS", "FRONT_TEST_STATUS"],
        PublishStatusAs = "UNIT_TEST_GATE_STATUS",
        Blocking = true
    };

    private string? Create(PipelineStepDefinition step) =>
        new PipelineGateStatusTaskFactory(_taskBuilder).CreateGateStatusTask(
            runId: 42,
            legServer: new Server { Id = 1, Name = "runner" },
            stepRun: new PipelineStepRun { StepName = step.Name },
            stepDef: step,
            legVars: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            stageDef: new PipelineStageDefinition { Name = "gate" });

    private Dictionary<string, string> CapturedVariables()
    {
        var call = _taskBuilder.ReceivedCalls().Single(
            c => c.GetMethodInfo().Name == nameof(IPipelineStepTaskBuilder.TrackTypedTask));
        return (Dictionary<string, string>)call.GetArguments()[5]!;
    }

    [Fact]
    public void AValidGateDispatchesThePipelineGateStatusOperation()
    {
        Assert.Null(Create(Step()));

        _taskBuilder.Received(1).TrackTypedTask(
            42, Arg.Any<Server>(), Arg.Any<PipelineStepRun>(), Arg.Any<PipelineStepDefinition>(),
            string.Empty, Arg.Any<Dictionary<string, string>>(), OperationKind.PipelineGateStatus);
    }

    [Fact]
    public void TheStatusesTravelAsOneCommaSeparatedList()
    {
        Create(Step());

        Assert.Equal(
            "BACK_TEST_STATUS,FRONT_TEST_STATUS",
            CapturedVariables()[PipelineGateStatusVariables.StatusVariables]);
        Assert.Equal("true", CapturedVariables()[PipelineGateStatusVariables.Blocking]);
        Assert.Equal(
            "UNIT_TEST_GATE_STATUS", CapturedVariables()[PipelineGateStatusVariables.PublishAs]);
    }

    [Fact]
    public void TheStepNameLabelsTheGateWhenNoLabelIsGiven()
    {
        Create(Step());

        Assert.Equal("Blocking test gate", CapturedVariables()[PipelineGateStatusVariables.Label]);
    }

    [Fact]
    public void AGateOverNoStatusIsRefusedBecauseItWouldAlwaysPass()
    {
        var error = Create(Step() with { StatusVariables = [] });

        Assert.Contains("always passes", error!, StringComparison.Ordinal);
        _taskBuilder.DidNotReceiveWithAnyArgs().TrackTypedTask(
            default, default!, default!, default!, default!, default!, default);
    }

    [Fact]
    public void AListOfBlanksIsAGateOverNoStatusToo()
    {
        var error = Create(Step() with { StatusVariables = ["", "   "] });

        Assert.Contains("always passes", error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("lower_case")]
    [InlineData("HAS SPACE")]
    [InlineData("1LEADING")]
    public void AStatusNameThatCannotBeARunVariableIsRefused(string name)
    {
        // A name no step can ever publish under would read as permanently missing, which the gate
        // reports as a failure forever - a definition error worth naming at dispatch.
        var error = Create(Step() with { StatusVariables = [name] });

        Assert.Contains("run variable name", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void APublishTargetThatCannotBeARunVariableIsRefused()
    {
        var error = Create(Step() with { PublishStatusAs = "not a name" });

        Assert.Contains("publish_status_as", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void AGateThatOnlyReportsNeedsNoPublishTarget()
    {
        Assert.Null(Create(Step() with { PublishStatusAs = null }));

        Assert.False(CapturedVariables().ContainsKey(PipelineGateStatusVariables.PublishAs));
    }
}
