// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests;

public class PermissionRepositoryOrgTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PermissionRepository _repo;

    public PermissionRepositoryOrgTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new PermissionRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task IsUserInResourceOrganizationAsync_Member_ReturnsTrue()
    {
        _db.Users.Add(new User { Id = 1, Username = "bob", IsActive = true });
        _db.Servers.Add(new Server { Id = 10, Name = "web", Hostname = "web.local", OrganizationId = 5, Status = ServerStatus.Online });
        _db.OrganizationMembers.Add(new OrganizationMember { Id = 1, UserId = 1, OrganizationId = 5, Role = OrganizationRole.Member });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await _repo.IsUserInResourceOrganizationAsync("bob", ResourceType.Server, 10, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsUserInResourceOrganizationAsync_NotMember_ReturnsFalse()
    {
        _db.Users.Add(new User { Id = 1, Username = "bob", IsActive = true });
        _db.Servers.Add(new Server { Id = 10, Name = "web", Hostname = "web.local", OrganizationId = 5, Status = ServerStatus.Online });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(await _repo.IsUserInResourceOrganizationAsync("bob", ResourceType.Server, 10, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsUserInResourceOrganizationAsync_ResourceMissing_ReturnsFalse()
        => Assert.False(await _repo.IsUserInResourceOrganizationAsync("bob", ResourceType.Project, 999, ct: TestContext.Current.CancellationToken));

    [Fact]
    public async Task IsUserInResourceOrganizationAsync_UnsupportedResourceType_ReturnsFalse()
        => Assert.False(await _repo.IsUserInResourceOrganizationAsync("bob", ResourceType.Pipeline, 1, ct: TestContext.Current.CancellationToken));

    [Fact]
    public async Task GetResourceIdsByOrganizationsAsync_EmptyOrgs_ReturnsEmpty()
        => Assert.Empty(await _repo.GetResourceIdsByOrganizationsAsync(ResourceType.Server, [], ct: TestContext.Current.CancellationToken));

    [Fact]
    public async Task GetResourceIdsByOrganizationsAsync_Server_ReturnsIdsInOrgs()
    {
        _db.Servers.AddRange(
            new Server { Id = 10, Name = "a", Hostname = "a", OrganizationId = 5, Status = ServerStatus.Online },
            new Server { Id = 11, Name = "b", Hostname = "b", OrganizationId = 6, Status = ServerStatus.Online },
            new Server { Id = 12, Name = "c", Hostname = "c", OrganizationId = 9, Status = ServerStatus.Online });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var ids = await _repo.GetResourceIdsByOrganizationsAsync(ResourceType.Server, [5, 6], ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, ids.Count);
        Assert.DoesNotContain(12, ids);
    }

    [Fact]
    public async Task GetResourceIdsByOrganizationsAsync_Project_ReturnsIdsInOrgs()
    {
        _db.Projects.AddRange(
            new Project { Id = 1, Name = "p1", OrganizationId = 5 },
            new Project { Id = 2, Name = "p2", OrganizationId = 7 });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var ids = await _repo.GetResourceIdsByOrganizationsAsync(ResourceType.Project, [5], ct: TestContext.Current.CancellationToken);

        Assert.Single(ids);
        Assert.Contains(1, ids);
    }

    [Fact]
    public async Task GetResourceIdsByOrganizationsAsync_UnsupportedType_ReturnsEmpty()
        => Assert.Empty(await _repo.GetResourceIdsByOrganizationsAsync(ResourceType.Pipeline, [5], ct: TestContext.Current.CancellationToken));
}
