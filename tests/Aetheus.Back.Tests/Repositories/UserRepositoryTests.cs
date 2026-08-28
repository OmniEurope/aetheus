// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Users;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class UserRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly UserRepository _repo;

    public UserRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new UserRepository(_db);
    }

    [Fact]
    public async Task GetUsersPagedAsync_ReturnsPagedOrderedByUsername()
    {
        _db.Users.AddRange(
            new User { Username = "zeta", PasswordHash = "h" },
            new User { Username = "alpha", PasswordHash = "h" },
            new User { Username = "mid", PasswordHash = "h" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetUsersPagedAsync(null, 1, 2, ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, total);
        Assert.Equal(2, items.Count);
        Assert.Equal("alpha", items[0].Username);
    }

    [Fact]
    public async Task GetUsersPagedAsync_WithSearch_FiltersUsernameAndEmail()
    {
        _db.Users.AddRange(
            new User { Username = "admin", PasswordHash = "h", Email = "admin@test.com" },
            new User { Username = "user1", PasswordHash = "h", Email = "user@test.com" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetUsersPagedAsync("admin", 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
        Assert.Equal("admin", items[0].Username);
    }

    [Fact]
    public async Task GetUsersPagedAsync_SearchByEmail()
    {
        _db.Users.AddRange(
            new User { Username = "u1", PasswordHash = "h", Email = "special@corp.com" },
            new User { Username = "u2", PasswordHash = "h", Email = "generic@test.com" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetUsersPagedAsync("special", 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetUserDetailAsync_Found_IncludesRoles()
    {
        var role = new Role { Name = "Admin", Description = "Administrator" };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var user = new User { Username = "u1", PasswordHash = "h" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetUserDetailAsync(user.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Single(result.UserRoles);
        Assert.Equal("Admin", result.UserRoles[0].Role.Name);
    }

    [Fact]
    public async Task GetUserDetailAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetUserDetailAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindByUsernameAsync_Found()
    {
        _db.Users.Add(new User { Username = "admin", PasswordHash = "h" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindByUsernameAsync("admin", ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FindByUsernameAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.FindByUsernameAsync("nonexistent", ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindByUsernameAsync_LegacyMixedCaseUsername_IsFoundCaseInsensitively()
    {
        _db.Users.Add(new User { Username = "LegacyUser", PasswordHash = "h" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindByUsernameAsync(
            "legacyuser", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("LegacyUser", result.Username);
    }

    [Fact]
    public async Task FindUserAsync_Found()
    {
        var user = new User { Username = "u1", PasswordHash = "h" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindUserAsync(user.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task AddUserAsync_Persists()
    {
        await _repo.AddUserAsync(new User { Username = "new", PasswordHash = "h" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.Users.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveUserAsync_Removes()
    {
        var user = new User { Username = "del", PasswordHash = "h" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveUserAsync(user, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.Users.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetRolesByNamesAsync_ReturnsMatchingRoles()
    {
        _db.Roles.AddRange(
            new Role { Name = "Admin", Description = "d" },
            new Role { Name = "User", Description = "d" },
            new Role { Name = "Viewer", Description = "d" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetRolesByNamesAsync(["Admin", "Viewer"], ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetAllRoleNamesAsync_ReturnsOrderedRoleNames()
    {
        _db.Roles.AddRange(
            new Role { Name = "Viewer", Description = "d" },
            new Role { Name = "Admin", Description = "d" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetAllRoleNamesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal("Admin", result[0]);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.Users.Add(new User { Username = "p", PasswordHash = "h" });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.Users.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
