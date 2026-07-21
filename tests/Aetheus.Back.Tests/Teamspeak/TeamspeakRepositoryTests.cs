// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Teamspeak;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Teamspeak;

public sealed class TeamspeakRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TeamspeakRepository _repository;

    public TeamspeakRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repository = new TeamspeakRepository(_db);
    }

    [Fact]
    public async Task GetChannelsPagedAsync_ReturnsRequestedPageAndServerTotal()
    {
        _db.TeamspeakChannels.AddRange(
            new TeamspeakChannel { ServerId = 1, ChannelId = 1, Name = "Alpha" },
            new TeamspeakChannel { ServerId = 1, ChannelId = 2, Name = "Beta" },
            new TeamspeakChannel { ServerId = 2, ChannelId = 1, Name = "Other" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repository.GetChannelsPagedAsync(
            1, null, 2, 1, "Name", false, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, total);
        Assert.Equal("Beta", Assert.Single(items).Name);
    }

    [Fact]
    public async Task GetClientsPagedAsync_ExcludesServerQueryClients()
    {
        _db.TeamspeakClients.AddRange(
            new TeamspeakClient { ServerId = 1, ClientId = 1, Nickname = "Alice" },
            new TeamspeakClient { ServerId = 1, ClientId = 2, Nickname = "Query", IsServerQuery = true });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repository.GetClientsPagedAsync(
            1, null, 1, 25, "Nickname", false, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, total);
        Assert.Equal("Alice", Assert.Single(items).Nickname);
    }

    [Fact]
    public async Task GetBansPagedAsync_ReturnsNewestFirst()
    {
        _db.TeamspeakBans.AddRange(
            new TeamspeakBan { ServerId = 1, BanId = 1, Nickname = "Old", Created = 10 },
            new TeamspeakBan { ServerId = 1, BanId = 2, Nickname = "New", Created = 20 });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repository.GetBansPagedAsync(
            1, null, 1, 1, "Created", true, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, total);
        Assert.Equal(2, Assert.Single(items).BanId);
    }

    public void Dispose() => _db.Dispose();
}
