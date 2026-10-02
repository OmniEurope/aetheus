// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Organizations;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests;

public class OrganizationRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly OrganizationRepository _repo;

    public OrganizationRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new OrganizationRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    private Organization SeedOrg(int id, string name, string slug)
    {
        var org = new Organization { Id = id, Name = name, Slug = slug };
        _db.Organizations.Add(org);
        _db.SaveChanges();
        return org;
    }

    [Fact]
    public async Task GetPagedAsync_NoSearch_ReturnsOrderedByName()
    {
        SeedOrg(1, "Zeta", "zeta");
        SeedOrg(2, "Alpha", "alpha");

        var (items, total) = await _repo.GetPagedAsync(null, 1, 10, null, false, TestContext.Current.CancellationToken);

        Assert.Equal(2, total);
        Assert.Equal("Alpha", items[0].Name);
    }

    [Fact]
    public async Task GetByIdAsync_FoundAndNotFound()
    {
        SeedOrg(1, "Org", "org");

        Assert.NotNull(await _repo.GetByIdAsync(1, TestContext.Current.CancellationToken));
        Assert.Null(await _repo.GetByIdAsync(99, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetWithMembersAndProjectsAsync_IncludesGraph()
    {
        SeedOrg(1, "Org", "org");
        _db.Users.Add(new User { Id = 1, Username = "bob", IsActive = true });
        _db.OrganizationMembers.Add(new OrganizationMember { Id = 1, OrganizationId = 1, UserId = 1, Role = OrganizationRole.Member });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var org = await _repo.GetWithMembersAndProjectsAsync(1, TestContext.Current.CancellationToken);

        Assert.NotNull(org);
        Assert.Single(org.Members);
    }

    [Fact]
    public async Task NameExists_And_SlugExists_RespectExcludeId()
    {
        SeedOrg(1, "Org", "org");

        Assert.True(await _repo.NameExistsAsync("Org", null, TestContext.Current.CancellationToken));
        Assert.False(await _repo.NameExistsAsync("Org", 1, TestContext.Current.CancellationToken)); // exclude self
        Assert.True(await _repo.SlugExistsAsync("org", null, TestContext.Current.CancellationToken));
        Assert.False(await _repo.SlugExistsAsync("org", 1, TestContext.Current.CancellationToken));
        Assert.False(await _repo.SlugExistsAsync("missing", null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddAndDelete_RoundTrips()
    {
        await _repo.AddAsync(new Organization { Id = 5, Name = "New", Slug = "new" }, TestContext.Current.CancellationToken);
        await _repo.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(await _repo.GetByIdAsync(5, TestContext.Current.CancellationToken));

        Assert.True(await _repo.DeleteAsync(5, TestContext.Current.CancellationToken));
        Assert.Null(await _repo.GetByIdAsync(5, TestContext.Current.CancellationToken));
        Assert.False(await _repo.DeleteAsync(5, TestContext.Current.CancellationToken)); // already gone
    }

    [Fact]
    public async Task Members_AddGetRemove()
    {
        SeedOrg(1, "Org", "org");
        _db.Users.Add(new User { Id = 1, Username = "bob", IsActive = true });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.AddMemberAsync(new OrganizationMember { Id = 1, OrganizationId = 1, UserId = 1, Role = OrganizationRole.Owner }, TestContext.Current.CancellationToken);
        await _repo.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(await _repo.GetMemberAsync(1, 1, TestContext.Current.CancellationToken));
        Assert.NotNull(await _repo.GetMemberByIdAsync(1, TestContext.Current.CancellationToken));
        Assert.NotNull(await _repo.GetMemberWithUserByIdAsync(1, 1, TestContext.Current.CancellationToken));
        Assert.NotNull(await _repo.GetUserByIdAsync(1, TestContext.Current.CancellationToken));

        Assert.True(await _repo.RemoveMemberAsync(1, 1, TestContext.Current.CancellationToken));
        Assert.Null(await _repo.GetMemberAsync(1, 1, TestContext.Current.CancellationToken));
        Assert.False(await _repo.RemoveMemberAsync(1, 99, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Projects_CountAndAssign()
    {
        SeedOrg(1, "Org", "org");
        SeedOrg(2, "Other", "other");
        _db.Projects.AddRange(
            new Project { Id = 1, Name = "P1", OrganizationId = 2 },
            new Project { Id = 2, Name = "P2", OrganizationId = 2 });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, await _repo.CountExistingProjectsAsync([1, 2, 99], TestContext.Current.CancellationToken));

        await _repo.AssignProjectsAsync(1, [1, 2], TestContext.Current.CancellationToken);

        var reassigned = await _db.Projects.AsNoTracking().Where(p => p.OrganizationId == 1).CountAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, reassigned);
    }

    [Fact]
    public async Task GetDefaultOrganizationIdAsync_ReturnsAetheusSlugOrg()
    {
        SeedOrg(1, "Other", "other");
        SeedOrg(2, "Aetheus", "aetheus");

        Assert.Equal(2, await _repo.GetDefaultOrganizationIdAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetOrganizationsForUsername_And_IdsForUser()
    {
        SeedOrg(1, "Org1", "org1");
        SeedOrg(2, "Org2", "org2");
        _db.Users.Add(new User { Id = 1, Username = "bob", IsActive = true });
        _db.OrganizationMembers.AddRange(
            new OrganizationMember { Id = 1, OrganizationId = 1, UserId = 1, Role = OrganizationRole.Owner },
            new OrganizationMember { Id = 2, OrganizationId = 2, UserId = 1, Role = OrganizationRole.Member });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var orgs = await _repo.GetOrganizationsForUsernameAsync("bob", TestContext.Current.CancellationToken);
        Assert.Equal(2, orgs.Count);

        var ids = await _repo.GetOrganizationIdsForUserAsync(1, TestContext.Current.CancellationToken);
        Assert.Equal(2, ids.Count);
    }
}
