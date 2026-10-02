// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Executors;
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// The aggregation is three string comparisons. What these pin is the case that decides whether a
/// gate is worth anything: a status nobody published must read as a FAILURE.
///
/// Shell defaults the other way. An unset variable expands to the empty string, so `[ "$S" = 0 ]` is
/// false and `[ "$S" != 1 ]` is true, and which of those a project happened to write decides whether
/// its gate holds or passes green on evidence that never arrived.
/// </summary>
public sealed class PipelineGateStatusOperationExecutorTests
{
    private readonly List<string> _output = [];

    private async Task<ExecutorResult> RunAsync(
        string names,
        Dictionary<string, string>? statuses = null,
        string? publishAs = null,
        bool blocking = true)
    {
        var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [PipelineGateStatusVariables.StatusVariables] = names,
            [PipelineGateStatusVariables.Blocking] = blocking ? "true" : "false",
            [PipelineGateStatusVariables.Label] = "Unit gate"
        };
        if (publishAs is not null) envVars[PipelineGateStatusVariables.PublishAs] = publishAs;
        foreach (var (key, value) in statuses ?? []) envVars[key] = value;

        return await new PipelineGateStatusOperationExecutor().ExecuteAsync(
            OperationKind.PipelineGateStatus,
            target: string.Empty,
            envVars: envVars,
            timeoutSeconds: 60,
            onOutput: (line, _) =>
            {
                _output.Add(line);
                return Task.CompletedTask;
            },
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private string? Published(string name) => _output
        .FirstOrDefault(line => line.StartsWith($"##aetheus[setvariable name={name}]", StringComparison.Ordinal))
        ?.Split(']')[^1];

    // --- The case the step exists for ---

    [Fact]
    public async Task AStatusNobodyPublishedFailsTheGateRatherThanPassingIt()
    {
        var result = await RunAsync("A,B", new() { ["A"] = "0" });

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(_output, line => line.Contains("never published", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AStatusPublishedEmptyIsTreatedAsAbsent()
    {
        var result = await RunAsync("A", new() { ["A"] = "   " });

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(_output, line => line.Contains("never published", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AGateNamingNoStatusIsRefusedInsteadOfPassingUnconditionally()
    {
        var result = await RunAsync("");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("empty-gate", result.FailureCode);
    }

    // --- Ordinary aggregation ---

    [Fact]
    public async Task AllStatusesPassingIsAPassingGate()
    {
        var result = await RunAsync("A,B,C", new() { ["A"] = "0", ["B"] = "0", ["C"] = "0" });

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task OneFailingStatusFailsTheGate()
    {
        var result = await RunAsync("A,B", new() { ["A"] = "0", ["B"] = "1" });

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("gate-failed", result.FailureCode);
    }

    [Fact]
    public async Task TheVerdictIsPublishedForWhoeverReadsItNext()
    {
        await RunAsync("A,B", new() { ["A"] = "0", ["B"] = "1" }, publishAs: "UNIT_GATE");

        Assert.Equal("1", Published("UNIT_GATE"));
    }

    [Fact]
    public async Task APassingVerdictIsPublishedToo()
    {
        await RunAsync("A", new() { ["A"] = "0" }, publishAs: "UNIT_GATE");

        Assert.Equal("0", Published("UNIT_GATE"));
    }

    // --- Evidence the gate cannot read ---

    [Fact]
    public async Task AStatusThatIsNeitherZeroNorOneIsRefusedNotCountedAsAFailure()
    {
        // Rounding unknown input to "failed" hides a broken producer just as surely as rounding it
        // to "passed" hides a broken gate.
        var result = await RunAsync("A", new() { ["A"] = "yes" });

        Assert.Equal("invalid-status", result.FailureCode);
    }

    [Fact]
    public async Task AnUnreadableStatusStopsTheGateEvenWhenAnEarlierOneAlreadyFailed()
    {
        var result = await RunAsync("A,B", new() { ["A"] = "1", ["B"] = "2" });

        Assert.Equal("invalid-status", result.FailureCode);
    }

    // --- Advisory mode ---

    [Fact]
    public async Task AnAdvisoryGatePublishesItsVerdictWithoutFailingTheStep()
    {
        var result = await RunAsync(
            "A,B", new() { ["A"] = "0", ["B"] = "1" }, publishAs: "UNIT_GATE", blocking: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1", Published("UNIT_GATE"));
    }

    [Fact]
    public async Task AnAdvisoryGateStillReportsAMissingStatusInItsVerdict()
    {
        var result = await RunAsync("A,B", new() { ["A"] = "0" }, publishAs: "UNIT_GATE", blocking: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1", Published("UNIT_GATE"));
    }

    [Fact]
    public async Task ARepeatedStatusIsReadOnce()
    {
        await RunAsync("A,A", new() { ["A"] = "0" });

        Assert.Single(_output, line => line.Contains("A = 0", StringComparison.Ordinal));
    }
}
