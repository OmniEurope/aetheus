// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Agent.Core.Tests;

public class TeamspeakServerQueryOperationExecutorTests
{
    [Fact]
    public void CanHandle_OnlyTeamspeakServerQuery()
    {
        var sut = new TeamspeakServerQueryOperationExecutor(new StubQueryClient(null));
        Assert.True(sut.CanHandle(OperationKind.TeamspeakServerQuery));
        Assert.False(sut.CanHandle(OperationKind.ServiceRestart));
        Assert.False(sut.CanHandle(OperationKind.PipelinePublishComplexity));
    }

    // The agent must NOT report a failed ServerQuery action as green: TS3 signals the outcome with a
    // trailing `error id=N` line - only id=0 is success. A failed login (id=520) or a rejected command
    // is a real failure (anti-fake rule).
    [Theory]
    [InlineData("error id=0 msg=ok", true)]
    [InlineData("clid=42 client_nickname=Bob\nerror id=0 msg=ok", true)]
    [InlineData("error id=520 msg=invalid\\sloginname\\sor\\spassword", false)]
    [InlineData("error id=512 msg=invalid\\sclientID", false)]
    [InlineData("some banner with no error line", false)]
    [InlineData("", false)]
    public void ServerQueryReportsSuccess_ParsesTrailingErrorLine(string reply, bool expected)
    {
        Assert.Equal(expected, TeamspeakServerQueryOperationExecutor.ServerQueryReportsSuccess(reply));
    }

    [Fact]
    public void ServerQueryReportsSuccess_LastErrorLineWins()
    {
        // A multi-command batch ends with the final command's error line - that is the verdict.
        const string reply = "error id=0 msg=ok\nsome output\nerror id=768 msg=database\\sempty\\sresult\\sset";
        Assert.False(TeamspeakServerQueryOperationExecutor.ServerQueryReportsSuccess(reply));
    }

    private sealed class StubQueryClient(string? reply) : Aetheus.Agent.Core.Collectors.ITeamspeakQueryClient
    {
        public Task<string?> ExecuteAsync(int port, string commands, CancellationToken ct = default) => Task.FromResult(reply);
    }
}
