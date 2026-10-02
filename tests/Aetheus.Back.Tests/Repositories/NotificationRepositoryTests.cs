// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Notifications;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class NotificationRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly NotificationRepository _repo;

    public NotificationRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new NotificationRepository(_db);
    }

    [Fact]
    public async Task GetChannelsAsync_ReturnsOrderedByName_WithRules()
    {
        _db.NotificationChannels.AddRange(
            new NotificationChannel { Name = "Zeta", Type = NotificationChannelType.Email },
            new NotificationChannel { Name = "Alpha", Type = NotificationChannelType.Webhook }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetChannelsPagedAsync(null, 1, 25, "Name", false, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, total);
        Assert.Equal(2, items.Count);
        Assert.Equal("Alpha", items[0].Name);
    }

    [Fact]
    public async Task GetChannelWithRulesAsync_Found_IncludesRules()
    {
        var channel = new NotificationChannel { Name = "Ch1", Type = NotificationChannelType.Email };
        _db.NotificationChannels.Add(channel);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.NotificationRules.Add(new NotificationRule { NotificationChannelId = channel.Id, EventType = "PipelineCompleted" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetChannelWithRulesAsync(channel.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Single(result.Rules);
    }

    [Fact]
    public async Task GetChannelWithRulesAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetChannelWithRulesAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindChannelAsync_Found()
    {
        var channel = new NotificationChannel { Name = "Test", Type = NotificationChannelType.Email };
        _db.NotificationChannels.Add(channel);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindChannelAsync(channel.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FindChannelAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.FindChannelAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task AddChannelAsync_Persists()
    {
        await _repo.AddChannelAsync(new NotificationChannel { Name = "New", Type = NotificationChannelType.Email }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.NotificationChannels.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveChannelAsync_Removes()
    {
        var channel = new NotificationChannel { Name = "Del", Type = NotificationChannelType.Email };
        _db.NotificationChannels.Add(channel);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveChannelAsync(channel, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.NotificationChannels.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetRulesForEventAsync_ReturnsOnlyEnabledRulesWithEnabledChannels()
    {
        var enabledChannel = new NotificationChannel { Name = "Enabled", Type = NotificationChannelType.Email, IsEnabled = true };
        var disabledChannel = new NotificationChannel { Name = "Disabled", Type = NotificationChannelType.Email, IsEnabled = false };
        _db.NotificationChannels.AddRange(enabledChannel, disabledChannel);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.NotificationRules.AddRange(
            new NotificationRule { NotificationChannelId = enabledChannel.Id, EventType = "Deploy", IsEnabled = true },
            new NotificationRule { NotificationChannelId = enabledChannel.Id, EventType = "Deploy", IsEnabled = false },
            new NotificationRule { NotificationChannelId = disabledChannel.Id, EventType = "Deploy", IsEnabled = true },
            new NotificationRule { NotificationChannelId = enabledChannel.Id, EventType = "Other", IsEnabled = true }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetRulesForEventAsync("Deploy", ct: TestContext.Current.CancellationToken);
        Assert.Single(result);
    }

    [Fact]
    public async Task FindRuleAsync_Found_IncludesChannel()
    {
        var channel = new NotificationChannel { Name = "Ch", Type = NotificationChannelType.Email };
        _db.NotificationChannels.Add(channel);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var rule = new NotificationRule { NotificationChannelId = channel.Id, EventType = "Test" };
        _db.NotificationRules.Add(rule);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindRuleAsync(rule.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.NotNull(result.Channel);
    }

    [Fact]
    public async Task FindRuleAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.FindRuleAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task AddRuleAsync_Persists()
    {
        var channel = new NotificationChannel { Name = "Ch", Type = NotificationChannelType.Email };
        _db.NotificationChannels.Add(channel);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.AddRuleAsync(new NotificationRule { NotificationChannelId = channel.Id, EventType = "Test" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.NotificationRules.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveRuleAsync_Removes()
    {
        var channel = new NotificationChannel { Name = "Ch", Type = NotificationChannelType.Email };
        _db.NotificationChannels.Add(channel);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var rule = new NotificationRule { NotificationChannelId = channel.Id, EventType = "Test" };
        _db.NotificationRules.Add(rule);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveRuleAsync(rule, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.NotificationRules.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.NotificationChannels.Add(new NotificationChannel { Name = "Pending", Type = NotificationChannelType.Email });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.NotificationChannels.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
