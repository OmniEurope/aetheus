// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Environments;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Environment = Aetheus.Back.Data.Entities.Environment;

namespace Aetheus.Back.Tests;

public class EnvironmentRepositoryLinkTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly EnvironmentRepository _repo;

    public EnvironmentRepositoryLinkTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new EnvironmentRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetEnvironmentForDuplicationAsync_FoundAndMissing()
    {
        _db.Environments.Add(new Environment
        {
            Id = 1,
            Name = "prod",
            ProjectId = 1,
            Servers = [],
            Checks = [],
            Libraries = [],
            Vaults = [],
            Pipelines = [],
            LinkedProjectServers = []
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(await _repo.GetEnvironmentForDuplicationAsync(1, ct: TestContext.Current.CancellationToken));
        Assert.Null(await _repo.GetEnvironmentForDuplicationAsync(999, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NameExistsInProjectAsync_DetectsExisting()
    {
        _db.Environments.Add(new Environment { Id = 1, Name = "prod", ProjectId = 1 });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await _repo.NameExistsInProjectAsync("prod", 1, ct: TestContext.Current.CancellationToken));
        Assert.False(await _repo.NameExistsInProjectAsync("staging", 1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetProjectServerProjectIdAsync_ReturnsProjectIdOrNull()
    {
        _db.ProjectServers.Add(new ProjectServer { Id = 3, ProjectId = 9, Type = ProjectServerType.AgentServer, DisplayName = "ps", Host = "h" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(9, await _repo.GetProjectServerProjectIdAsync(3, ct: TestContext.Current.CancellationToken));
        Assert.Null(await _repo.GetProjectServerProjectIdAsync(999, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Link_AddExistsRemove_RoundTrips()
    {
        await _repo.AddLinkAsync(new EnvironmentProjectServer { EnvironmentId = 1, ProjectServerId = 5 }, ct: TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await _repo.LinkExistsAsync(1, 5, ct: TestContext.Current.CancellationToken));
        Assert.False(await _repo.LinkExistsAsync(1, 99, ct: TestContext.Current.CancellationToken));

        await _repo.RemoveLinkAsync(1, 5, ct: TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(await _repo.LinkExistsAsync(1, 5, ct: TestContext.Current.CancellationToken));
    }
}
