// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Dashboards;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class DashboardRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly DashboardRepository _repo;
    private readonly int _userId;

    public DashboardRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new DashboardRepository(_db);

        var user = new User { Username = "admin", PasswordHash = "h" };
        _db.Users.Add(user);
        _db.SaveChanges();
        _userId = user.Id;
    }

    [Fact]
    public async Task GetByUserIdAsync_ReturnsOrderedDashboards()
    {
        _db.Dashboards.AddRange(
            new Dashboard { UserId = _userId, Name = "Zeta" },
            new Dashboard { UserId = _userId, Name = "Alpha" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetByUserIdAsync(_userId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal("Alpha", result[0].Name);
    }

    [Fact]
    public async Task GetByUserIdAsync_FiltersByUserId()
    {
        var other = new User { Username = "other", PasswordHash = "h" };
        _db.Users.Add(other);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.Dashboards.AddRange(
            new Dashboard { UserId = _userId, Name = "Mine" },
            new Dashboard { UserId = other.Id, Name = "Theirs" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetByUserIdAsync(_userId, ct: TestContext.Current.CancellationToken);
        Assert.Single(result);
    }

    [Fact]
    public async Task GetDetailAsync_Found_IncludesWidgets()
    {
        var dash = new Dashboard { UserId = _userId, Name = "Dash" };
        _db.Dashboards.Add(dash);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.DashboardWidgets.Add(new DashboardWidget { DashboardId = dash.Id, Title = "W1", WidgetType = DashboardWidgetType.ServerCount });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetDetailAsync(dash.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Single(result.Widgets);
    }

    [Fact]
    public async Task GetDetailAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetDetailAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindAsync_Found()
    {
        var dash = new Dashboard { UserId = _userId, Name = "Dash" };
        _db.Dashboards.Add(dash);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindAsync(dash.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task AddAsync_Persists()
    {
        await _repo.AddAsync(new Dashboard { UserId = _userId, Name = "New" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.Dashboards.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveAsync_Removes()
    {
        var dash = new Dashboard { UserId = _userId, Name = "Del" };
        _db.Dashboards.Add(dash);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveAsync(dash, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.Dashboards.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.Dashboards.Add(new Dashboard { UserId = _userId, Name = "Pending" });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.Dashboards.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
