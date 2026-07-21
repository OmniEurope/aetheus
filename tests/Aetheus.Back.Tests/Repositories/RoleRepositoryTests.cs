// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Auth;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class RoleRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RoleRepository _repo;

    public RoleRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new RoleRepository(_db);
    }

    [Fact]
    public async Task GetRolesPagedAsync_ReturnsOrderedPageAndTotal()
    {
        _db.Roles.AddRange(
            new Role { Name = "Zeta", Description = "Z" },
            new Role { Name = "Alpha", Description = "A" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (result, total) = await _repo.GetRolesPagedAsync(
            search: null, page: 1, pageSize: 1, sortBy: "Name", sortDescending: false, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, total);
        Assert.Single(result);
        Assert.Equal("Alpha", result[0].Name);
    }

    [Fact]
    public async Task GetRolesPagedAsync_IncludesCountFields()
    {
        var role = new Role { Name = "Dev", Description = "Developer" };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.ResourcePermissions.Add(new ResourcePermission
        {
            RoleId = role.Id,
            ResourceType = ResourceType.Pipeline,
            Permission = Permission.Read
        });
        var user = new User { Username = "alice", PasswordHash = "x", IsActive = true };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.Set<UserRole>().Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (result, total) = await _repo.GetRolesPagedAsync(
            search: null, page: 1, pageSize: 25, sortBy: null, sortDescending: false, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, total);
        Assert.Equal(1, result[0].PermissionCount);
        Assert.Equal(1, result[0].UserCount);
    }

    [Fact]
    public async Task GetUsersInRolePagedAsync_ReturnsRequestedPageAndTotal()
    {
        var role = new Role { Name = "Operators", Description = "" };
        var alice = new User { Username = "alice", PasswordHash = "x", IsActive = true };
        var bob = new User { Username = "bob", PasswordHash = "x", IsActive = true };
        _db.AddRange(role, alice, bob);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.Set<UserRole>().AddRange(
            new UserRole { UserId = alice.Id, RoleId = role.Id },
            new UserRole { UserId = bob.Id, RoleId = role.Id });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetUsersInRolePagedAsync(
            role.Id, search: null, page: 2, pageSize: 1, sortBy: "Username", sortDescending: false, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, total);
        Assert.Single(items);
        Assert.Equal("bob", items[0].Username);
    }

    [Fact]
    public async Task GetUsersAvailableForRolePagedAsync_ExcludesMembers()
    {
        var role = new Role { Name = "Operators", Description = "" };
        var member = new User { Username = "member", PasswordHash = "x", IsActive = true };
        var available = new User { Username = "available", PasswordHash = "x", IsActive = true };
        _db.AddRange(role, member, available);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.Set<UserRole>().Add(new UserRole { UserId = member.Id, RoleId = role.Id });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetUsersAvailableForRolePagedAsync(
            role.Id, search: null, page: 1, pageSize: 25, sortBy: "Username", sortDescending: false, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, total);
        Assert.Equal("available", Assert.Single(items).Username);
    }

    [Fact]
    public async Task GetRoleAsync_Found_ReturnsWithPermissions()
    {
        var role = new Role { Name = "Admin", Description = "Admin" };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.ResourcePermissions.Add(new ResourcePermission
        {
            RoleId = role.Id,
            ResourceType = ResourceType.Server,
            Permission = Permission.Admin
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetRoleAsync(role.Id, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Admin", result.Name);
        Assert.Single(result.Permissions);
    }

    [Fact]
    public async Task GetRoleAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetRoleAsync(999, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRoleEntityAsync_Found_ReturnsEntity()
    {
        var role = new Role { Name = "Test", Description = "" };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetRoleEntityAsync(role.Id, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
    }

    [Fact]
    public async Task RoleNameExistsAsync_Exists_ReturnsTrue()
    {
        _db.Roles.Add(new Role { Name = "Admin", Description = "" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await _repo.RoleNameExistsAsync("Admin", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RoleNameExistsAsync_ExcludeId_ExcludesSelf()
    {
        var role = new Role { Name = "Admin", Description = "" };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(await _repo.RoleNameExistsAsync("Admin", role.Id, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateRoleAsync_CreatesAndReturns()
    {
        var result = await _repo.CreateRoleAsync("NewRole", "Desc", ct: TestContext.Current.CancellationToken);

        Assert.True(result.Id > 0);
        Assert.Equal("NewRole", result.Name);
        Assert.Equal(1, await _db.Roles.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateRoleAsync_UpdatesEntity()
    {
        var role = new Role { Name = "Old", Description = "" };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        role.Name = "New";
        await _repo.UpdateRoleAsync(role, ct: TestContext.Current.CancellationToken);

        var updated = await _db.Roles.FindAsync([role.Id], TestContext.Current.CancellationToken);
        Assert.Equal("New", updated!.Name);
    }

    [Fact]
    public async Task DeleteRoleAsync_RemovesEntity()
    {
        var role = new Role { Name = "ToDelete", Description = "" };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.DeleteRoleAsync(role, ct: TestContext.Current.CancellationToken);

        Assert.Equal(0, await _db.Roles.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetPermissionsForRoleAsync_ReturnsPermissions()
    {
        var role = new Role { Name = "Dev", Description = "" };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.ResourcePermissions.AddRange(
            new ResourcePermission { RoleId = role.Id, ResourceType = ResourceType.Pipeline, Permission = Permission.Read },
            new ResourcePermission { RoleId = role.Id, ResourceType = ResourceType.Server, Permission = Permission.Write }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetPermissionsForRoleAsync(role.Id, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task SetPermissionsForRoleAsync_ReplacesExisting()
    {
        var role = new Role { Name = "Dev", Description = "" };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.ResourcePermissions.Add(new ResourcePermission
        {
            RoleId = role.Id,
            ResourceType = ResourceType.Pipeline,
            Permission = Permission.Read
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var newPermissions = new List<ResourcePermissionEntry>
        {
            new() { ResourceType = ResourceType.Server, Permission = Permission.Admin },
            new() { ResourceType = ResourceType.Pipeline, Permission = Permission.Write }
        };
        await _repo.SetPermissionsForRoleAsync(role.Id, newPermissions, ct: TestContext.Current.CancellationToken);

        var result = await _db.ResourcePermissions.Where(rp => rp.RoleId == role.Id).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.ResourceType == ResourceType.Server);
    }

    [Fact]
    public async Task GetEffectivePermissionsAsync_ReturnsRolePermissions()
    {
        var role = new Role { Name = "Dev", Description = "" };
        _db.Roles.Add(role);
        var user = new User { Username = "bob", PasswordHash = "x", IsActive = true };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.Set<UserRole>().Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        _db.ResourcePermissions.Add(new ResourcePermission
        {
            RoleId = role.Id,
            ResourceType = ResourceType.Pipeline,
            Permission = Permission.Read
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetEffectivePermissionsAsync(user.Id, ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("Dev", result[0].GrantedByRole);
    }

    [Fact]
    public async Task GetUserPermissionsAsync_ReturnsPermissionsForActiveUser()
    {
        var role = new Role { Name = "Dev", Description = "" };
        _db.Roles.Add(role);
        var user = new User { Username = "carol", PasswordHash = "x", IsActive = true };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.Set<UserRole>().Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        _db.ResourcePermissions.Add(new ResourcePermission
        {
            RoleId = role.Id,
            ResourceType = ResourceType.Server,
            Permission = Permission.Write
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetUserPermissionsAsync("carol", ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(ResourceType.Server, result[0].ResourceType);
    }

    [Fact]
    public async Task GetUserPermissionsAsync_InactiveUser_ReturnsEmpty()
    {
        var role = new Role { Name = "Dev", Description = "" };
        _db.Roles.Add(role);
        var user = new User { Username = "inactive", PasswordHash = "x", IsActive = false };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.Set<UserRole>().Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        _db.ResourcePermissions.Add(new ResourcePermission
        {
            RoleId = role.Id,
            ResourceType = ResourceType.Server,
            Permission = Permission.Write
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetUserPermissionsAsync("inactive", ct: TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    public void Dispose() => _db.Dispose();
}
