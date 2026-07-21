// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Components.Servers.Events;
using Aetheus.Back.Hubs;
using Aetheus.Back.Services;
using Aetheus.Back.Services.DomainEvents;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class ServerTimeoutServiceTests
{
    private static (ServerTimeoutService Sut, IServerRepository Repo, IClientProxy Proxy, IDomainEventDispatcher Events)
        BuildSut(params Aetheus.Back.Data.Entities.Server[] stale)
    {
        var serverRepo = Substitute.For<IServerRepository>();
        var hubContext = Substitute.For<IHubContext<ServerHub>>();
        var hubClients = Substitute.For<IHubClients>();
        var clientProxy = Substitute.For<IClientProxy>();
        var events = Substitute.For<IDomainEventDispatcher>();

        hubContext.Clients.Returns(hubClients);
        hubClients.Groups(Arg.Any<IReadOnlyList<string>>()).Returns(clientProxy);

        serverRepo.GetStaleOnlineServersAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(stale.ToList());
        serverRepo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddScoped(_ => serverRepo);
        services.AddScoped(_ => hubContext);
        services.AddScoped(_ => events);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero));
        var sut = new ServerTimeoutService(scopeFactory, Substitute.For<ILogger<ServerTimeoutService>>(),
            Options.Create(new BackgroundServicesOptions()), clock);
        return (sut, serverRepo, clientProxy, events);
    }

    [Fact]
    public async Task CheckServerTimeouts_MarksStaleServersOffline_SavesAndBroadcasts()
    {
        var staleServer = new Aetheus.Back.Data.Entities.Server
        {
            Id = 1,
            Name = "stale-server",
            Status = ServerStatus.Online,
            LastHeartbeat = new DateTime(2026, 6, 16, 11, 55, 0, DateTimeKind.Utc)
        };
        var (sut, repo, proxy, events) = BuildSut(staleServer);

        await sut.CheckServerTimeouts(TestContext.Current.CancellationToken);

        Assert.Equal(ServerStatus.Offline, staleServer.Status);
        await repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await proxy.Received(1).SendCoreAsync("ServerOffline", Arg.Is<object?[]>(a => (int)a[0]! == 1), Arg.Any<CancellationToken>());
        events.Received(1).Publish(Arg.Is<ServerWentOfflineEvent>(e => e.ServerId == 1));
    }

    [Fact]
    public async Task CheckServerTimeouts_NoStaleServers_DoesNotSaveOrBroadcast()
    {
        var (sut, repo, proxy, events) = BuildSut();

        await sut.CheckServerTimeouts(TestContext.Current.CancellationToken);

        await repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await proxy.DidNotReceive().SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
        events.DidNotReceive().Publish(Arg.Any<ServerWentOfflineEvent>());
    }
}
