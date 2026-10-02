// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.ServiceConnections;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class ServiceConnectionRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ServiceConnectionRepository _repo;

    public ServiceConnectionRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new ServiceConnectionRepository(_db);
    }

    [Fact]
    public async Task GetPagedAsync_ReturnsPagedResults()
    {
        _db.ServiceConnections.AddRange(
            new ServiceConnection { Name = "Conn-A", Type = Aetheus.Shared.Components.ServiceConnections.ServiceConnectionType.GitHub },
            new ServiceConnection { Name = "Conn-B", Type = Aetheus.Shared.Components.ServiceConnections.ServiceConnectionType.GitLab },
            new ServiceConnection { Name = "Conn-C", Type = Aetheus.Shared.Components.ServiceConnections.ServiceConnectionType.DockerRegistry }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetPagedAsync(null, null, 1, 2, ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, total);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task GetPagedAsync_WithSearch_Filters()
    {
        _db.ServiceConnections.AddRange(
            new ServiceConnection { Name = "GitHub Token", Type = Aetheus.Shared.Components.ServiceConnections.ServiceConnectionType.GitHub },
            new ServiceConnection { Name = "Docker Hub", Type = Aetheus.Shared.Components.ServiceConnections.ServiceConnectionType.DockerRegistry }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetPagedAsync("GitHub", null, 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetPagedAsync_WithProjectId_Filters()
    {
        var p = new Project { Name = "P1" };
        _db.Projects.Add(p);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.ServiceConnections.AddRange(
            new ServiceConnection { Name = "A", Type = Aetheus.Shared.Components.ServiceConnections.ServiceConnectionType.GitHub, ProjectId = p.Id },
            new ServiceConnection { Name = "B", Type = Aetheus.Shared.Components.ServiceConnections.ServiceConnectionType.GitHub, ProjectId = 999 }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetPagedAsync(null, p.Id, 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetPagedAsync_WithAccessibleIds_Filters()
    {
        _db.ServiceConnections.AddRange(
            new ServiceConnection { Name = "A", Type = Aetheus.Shared.Components.ServiceConnections.ServiceConnectionType.GitHub },
            new ServiceConnection { Name = "B", Type = Aetheus.Shared.Components.ServiceConnections.ServiceConnectionType.GitLab }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var first = await _db.ServiceConnections.FirstAsync(sc => sc.Name == "A", cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetPagedAsync(null, null, 1, 10, [first.Id], ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetDetailAsync_Found_IncludesProject()
    {
        var p = new Project { Name = "P" };
        _db.Projects.Add(p);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var conn = new ServiceConnection { Name = "Conn", Type = Aetheus.Shared.Components.ServiceConnections.ServiceConnectionType.GitHub, ProjectId = p.Id };
        _db.ServiceConnections.Add(conn);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetDetailAsync(conn.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.NotNull(result.Project);
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
        var conn = new ServiceConnection { Name = "Conn", Type = Aetheus.Shared.Components.ServiceConnections.ServiceConnectionType.GitHub };
        _db.ServiceConnections.Add(conn);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindAsync(conn.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FindByNamesAsync_MatchesByNameAndProject()
    {
        var p = new Project { Name = "P" };
        _db.Projects.Add(p);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.ServiceConnections.AddRange(
            new ServiceConnection { Name = "Shared", Type = Aetheus.Shared.Components.ServiceConnections.ServiceConnectionType.GitHub, ProjectId = null },
            new ServiceConnection { Name = "Project", Type = Aetheus.Shared.Components.ServiceConnections.ServiceConnectionType.GitHub, ProjectId = p.Id },
            new ServiceConnection { Name = "Other", Type = Aetheus.Shared.Components.ServiceConnections.ServiceConnectionType.GitHub, ProjectId = 999 }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindByNamesAsync(["Shared", "Project", "Other"], p.Id, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task AddAsync_Persists()
    {
        await _repo.AddAsync(new ServiceConnection { Name = "New", Type = Aetheus.Shared.Components.ServiceConnections.ServiceConnectionType.GitHub }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.ServiceConnections.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveAsync_Removes()
    {
        var conn = new ServiceConnection { Name = "Del", Type = Aetheus.Shared.Components.ServiceConnections.ServiceConnectionType.GitHub };
        _db.ServiceConnections.Add(conn);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveAsync(conn, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.ServiceConnections.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.ServiceConnections.Add(new ServiceConnection { Name = "Pending", Type = Aetheus.Shared.Components.ServiceConnections.ServiceConnectionType.GitHub });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.ServiceConnections.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
