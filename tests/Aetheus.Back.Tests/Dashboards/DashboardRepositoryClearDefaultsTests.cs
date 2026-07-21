// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Dashboards;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests;

public class DashboardRepositoryClearDefaultsTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly DashboardRepository _repo;

    public DashboardRepositoryClearDefaultsTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new DashboardRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ClearDefaultsAsync_ClearsOnlyTargetUsersDefaults()
    {
        _db.Dashboards.AddRange(
            new Dashboard { Id = 1, UserId = 1, Name = "A", IsDefault = true },
            new Dashboard { Id = 2, UserId = 1, Name = "B", IsDefault = true },
            new Dashboard { Id = 3, UserId = 1, Name = "C", IsDefault = false },
            new Dashboard { Id = 4, UserId = 2, Name = "D", IsDefault = true });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.ClearDefaultsAsync(1, ct: TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var user1Defaults = await _db.Dashboards.AsNoTracking().CountAsync(d => d.UserId == 1 && d.IsDefault, cancellationToken: TestContext.Current.CancellationToken);
        var user2Defaults = await _db.Dashboards.AsNoTracking().CountAsync(d => d.UserId == 2 && d.IsDefault, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, user1Defaults);
        Assert.Equal(1, user2Defaults);
    }
}
