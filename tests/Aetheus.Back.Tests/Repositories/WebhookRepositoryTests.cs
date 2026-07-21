// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Webhooks;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class WebhookRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly WebhookRepository _repo;

    public WebhookRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new WebhookRepository(_db);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsOrderedByEventType()
    {
        _db.WebhookSubscriptions.AddRange(
            new WebhookSubscription { EventType = "pipeline.completed", TargetUrl = "https://a.com" },
            new WebhookSubscription { EventType = "build.started", TargetUrl = "https://b.com" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetAllAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal("build.started", result[0].EventType);
    }

    [Fact]
    public async Task GetEnabledByEventAsync_ReturnsOnlyEnabled()
    {
        _db.WebhookSubscriptions.AddRange(
            new WebhookSubscription { EventType = "deploy", TargetUrl = "https://a.com", IsEnabled = true },
            new WebhookSubscription { EventType = "deploy", TargetUrl = "https://b.com", IsEnabled = false },
            new WebhookSubscription { EventType = "other", TargetUrl = "https://c.com", IsEnabled = true }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetEnabledByEventAsync("deploy", ct: TestContext.Current.CancellationToken);
        Assert.Single(result);
        Assert.Equal("https://a.com", result[0].TargetUrl);
    }

    [Fact]
    public async Task FindAsync_Found()
    {
        var wh = new WebhookSubscription { EventType = "test", TargetUrl = "https://a.com" };
        _db.WebhookSubscriptions.Add(wh);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindAsync(wh.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FindAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.FindAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task AddAsync_Persists()
    {
        await _repo.AddAsync(new WebhookSubscription { EventType = "new", TargetUrl = "https://a.com" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.WebhookSubscriptions.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveAsync_Removes()
    {
        var wh = new WebhookSubscription { EventType = "del", TargetUrl = "https://a.com" };
        _db.WebhookSubscriptions.Add(wh);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveAsync(wh, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.WebhookSubscriptions.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.WebhookSubscriptions.Add(new WebhookSubscription { EventType = "pending", TargetUrl = "https://a.com" });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.WebhookSubscriptions.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
