// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Teamspeak;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class TeamspeakServiceTokenTests
{
    private readonly ITeamspeakRepository _repo = Substitute.For<ITeamspeakRepository>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly TeamspeakService _sut;

    public TeamspeakServiceTokenTests()
    {
        var encryption = Substitute.For<IEncryptionService>();
        encryption.EncryptValue(Arg.Any<string>()).Returns(ci => "ENC:" + ci.Arg<string>());
        var serverRepo = Substitute.For<Aetheus.Back.Components.Servers.IServerRepository>();
        _sut = new TeamspeakService(_repo, serverRepo, _audit, encryption, Substitute.For<Aetheus.Back.Components.Tasks.ITaskService>());
    }

    [Fact]
    public async Task CreateToken_NotInstalled_ThrowsBadRequest()
    {
        _repo.GetStateAsync(1, Arg.Any<CancellationToken>()).Returns((TeamspeakState?)null);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.CreateTokenAsync(1, new TeamspeakTokenCreateRequest { Type = 0, GroupId = 7 }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateToken_Installed_QueuesServerQueryTaskAndAudits()
    {
        Installed();

        await _sut.CreateTokenAsync(1, new TeamspeakTokenCreateRequest { Type = 0, GroupId = 7, Description = "admin token" }, ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).AddTaskAsync(Arg.Any<ServerTask>(), Arg.Any<CancellationToken>());
        // Audit logs only the non-sensitive type/group reference, never the token description.
        await _audit.Received(1).LogAsync("TeamspeakTokenAdd", "Teamspeak", 1,
            Arg.Is<string>(d => d.Contains("type=0") && !d.Contains("admin token")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MoveClient_WithPassword_QueuesSensitiveTaskAndAudits()
    {
        Installed();

        await _sut.MoveClientAsync(1, new TeamspeakMoveClientRequest
        {
            ClientId = 5,
            TargetChannelId = 9,
            ChannelPassword = "secret"
        }, ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).AddTaskAsync(Arg.Any<ServerTask>(), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync("TeamspeakMove", "Teamspeak", 1, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteComplaint_AllForTarget_QueuesTaskAndAudits()
    {
        Installed();

        await _sut.DeleteComplaintAsync(1, new TeamspeakComplaintDeleteRequest
        {
            TargetClientDatabaseId = 42,
            SourceClientDatabaseId = null
        }, ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).AddTaskAsync(Arg.Any<ServerTask>(), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync("TeamspeakComplaintDelete", "Teamspeak", 1, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddClientToServerGroup_QueuesTaskAndAudits()
    {
        Installed();

        await _sut.AddClientToServerGroupAsync(1, new TeamspeakServerGroupAddRequest
        {
            ServerGroupId = 6,
            ClientDatabaseId = 42
        }, ct: TestContext.Current.CancellationToken);

        await _audit.Received(1).LogAsync("TeamspeakServerGroupAdd", "Teamspeak", 1, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveClientFromServerGroup_QueuesTaskAndAudits()
    {
        Installed();

        await _sut.RemoveClientFromServerGroupAsync(1, new TeamspeakServerGroupRemoveRequest
        {
            ServerGroupId = 6,
            ClientDatabaseId = 42
        }, ct: TestContext.Current.CancellationToken);

        await _audit.Received(1).LogAsync("TeamspeakServerGroupRemove", "Teamspeak", 1, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListServerGroups_QueuesQueryTask()
    {
        Installed();
        await _sut.ListServerGroupsAsync(1, ct: TestContext.Current.CancellationToken);
        await AssertQueryTaskQueued("servergrouplist");
    }

    [Fact]
    public async Task ListTokens_QueuesQueryTask()
    {
        Installed();
        await _sut.ListTokensAsync(1, ct: TestContext.Current.CancellationToken);
        await AssertQueryTaskQueued("tokenlist");
    }

    [Fact]
    public async Task GetServerInfo_QueuesQueryTask()
    {
        Installed();
        await _sut.GetServerInfoAsync(1, ct: TestContext.Current.CancellationToken);
        await AssertQueryTaskQueued("serverinfo");
    }

    [Fact]
    public async Task ListComplaints_QueuesQueryTask()
    {
        Installed();
        await _sut.ListComplaintsAsync(1, ct: TestContext.Current.CancellationToken);
        await AssertQueryTaskQueued("complainlist");
    }

    // Assert the real task contract, not just "AddTaskAsync was called once": the correct typed
    // OperationKind (TeamspeakServerQuery), the 15s query timeout, and the command keyword identifying
    // the specific ServerQuery verb - so a regression that mis-routes one query to another is caught.
    private async Task AssertQueryTaskQueued(string commandKeyword) =>
        await _repo.Received(1).AddTaskAsync(
            Arg.Is<ServerTask>(t => t.ServerId == 1
                && t.Executor == Aetheus.Shared.Enums.ExecutorType.Operation
                && t.Operation == Aetheus.Shared.Enums.OperationKind.TeamspeakServerQuery
                && t.TimeoutSeconds == 15
                && t.Command != null && t.Command.Contains(commandKeyword)),
            Arg.Any<CancellationToken>());

    [Fact]
    public async Task ListServerGroups_NotInstalled_Throws()
    {
        _repo.GetStateAsync(1, Arg.Any<CancellationToken>()).Returns((TeamspeakState?)null);
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ListServerGroupsAsync(1, ct: TestContext.Current.CancellationToken));
    }

    private void Installed() =>
        _repo.GetStateAsync(1, Arg.Any<CancellationToken>())
            .Returns(new TeamspeakState { ServerId = 1, QueryPort = 10011 });
}
