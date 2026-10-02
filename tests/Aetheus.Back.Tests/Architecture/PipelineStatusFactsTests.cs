// SPDX-License-Identifier: EUPL-1.2


namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// PLAN-003 D13 added a sixth status. Nine call sites used to spell "the run is over" as
/// <c>is Success or Failed or Cancelled</c>, so the new value would have read as "still running" to
/// the finalizer, the rerun button, the release rollback and the trigger reconciler. These pin the
/// single definition, and the sweep below is what makes the next added value impossible to forget.
/// </summary>
public sealed class PipelineStatusFactsTests
{
    [Theory]
    [InlineData(PipelineStatus.Success)]
    [InlineData(PipelineStatus.Failed)]
    [InlineData(PipelineStatus.Cancelled)]
    [InlineData(PipelineStatus.Partial)]
    public void Terminal_statuses_are_terminal(PipelineStatus status) => Assert.True(status.IsTerminal());

    [Theory]
    [InlineData(PipelineStatus.Pending)]
    [InlineData(PipelineStatus.Running)]
    [InlineData(PipelineStatus.WaitingForApproval)]
    public void Live_statuses_are_not_terminal(PipelineStatus status) => Assert.False(status.IsTerminal());

    [Fact]
    public void Only_Success_is_a_successful_ending()
    {
        Assert.False(PipelineStatus.Success.IsUnsuccessful());
        Assert.True(PipelineStatus.Partial.IsUnsuccessful());
        Assert.True(PipelineStatus.Failed.IsUnsuccessful());
        Assert.True(PipelineStatus.Cancelled.IsUnsuccessful());

        // A run still going is not an unsuccessful ending: it has not ended.
        Assert.False(PipelineStatus.Running.IsUnsuccessful());
    }

    [Fact]
    public void No_status_enumerates_the_terminal_set_by_hand()
    {
        // The literal that used to be duplicated everywhere. Left anywhere in the sources, it means a
        // call site that will silently ignore the next status added to the enum.
        var offenders = RepositoryScan
            .Enumerate(Path.Combine(RepositoryScan.Root, "src"), "*.cs")
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.EndsWith("PipelineStatusFacts.cs", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains(
                "PipelineStatus.Success or PipelineStatus.Failed or PipelineStatus.Cancelled",
                StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"These files decide 'the run is over' on their own instead of PipelineStatusFacts.IsTerminal: {string.Join(", ", offenders)}");
    }
}
