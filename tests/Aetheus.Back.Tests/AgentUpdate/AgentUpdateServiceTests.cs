// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AgentUpdate;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Hubs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class AgentUpdateServiceTests
{
    private readonly IServerRepository _repo = Substitute.For<IServerRepository>();
    private readonly IHubContext<ServerHub> _hubMock = Substitute.For<IHubContext<ServerHub>>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly Aetheus.Back.Components.Tasks.ITaskService _taskService = Substitute.For<Aetheus.Back.Components.Tasks.ITaskService>();
    private readonly AgentUpdateService _sut;

    public AgentUpdateServiceTests()
    {
        var clientProxy = Substitute.For<IClientProxy>();
        var hubClients = Substitute.For<IHubClients>();
        hubClients.Group(Arg.Any<string>()).Returns(clientProxy);
        hubClients.Groups(Arg.Any<IReadOnlyList<string>>()).Returns(clientProxy);
        _hubMock.Clients.Returns(hubClients);

        _sut = new AgentUpdateService(_repo, _hubMock, _taskService, _audit);
    }

    [Fact]
    public async Task QueueUpdateAsync_ServerExists_QueuesTaskAndAudits()
    {
        // Item #7: QueueUpdateAsync now resolves the server via FindServerAsync (it needs the
        // name for the TaskQueued broadcast). Setup must return a Server entity, not just a bool.
        _repo.FindServerAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 1, Name = "srv-1", Hostname = "h" });
        _repo.AddTaskAsync(Arg.Any<ServerTask>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await _sut.QueueUpdateAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        await _repo.Received(1).AddTaskAsync(Arg.Is<ServerTask>(t =>
            t.ServerId == 1 && t.Operation == OperationKind.AgentSelfUpdate), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(Arg.Any<string>(), "Server", 1, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueueUpdateAsync_ServerNotFound_ThrowsNotFound()
    {
        _repo.FindServerAsync(99, Arg.Any<CancellationToken>()).Returns((Server?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.QueueUpdateAsync(99, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task QueueUpdateAllAsync_EmptyList_ReturnsEmptyResponse()
    {
        var result = await _sut.QueueUpdateAllAsync([], ct: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.QueuedCount);
    }

    [Fact]
    public async Task QueueUpdateAllAsync_WithServers_QueuesAll()
    {
        _repo.GetServerIdNamePairsAsync(Arg.Any<List<int>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<(int Id, string Name)> { (1, "srv-1"), (2, "srv-2") });
        _repo.AddTasksAsync(Arg.Any<IEnumerable<ServerTask>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await _sut.QueueUpdateAllAsync([1, 2], ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.QueuedCount);
        await _repo.Received(1).AddTasksAsync(Arg.Any<IEnumerable<ServerTask>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueueUpdateAllAsync_NullList_PassesNull()
    {
        _repo.GetServerIdNamePairsAsync(null, Arg.Any<CancellationToken>())
            .Returns(new List<(int Id, string Name)> { (1, "srv-1") });
        _repo.AddTasksAsync(Arg.Any<IEnumerable<ServerTask>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await _sut.QueueUpdateAllAsync(null, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.QueuedCount);
    }
}
