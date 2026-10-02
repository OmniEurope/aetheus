// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.WorkItems;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class WorkItemRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly WorkItemRepository _repo;
    private readonly int _projectId;

    public WorkItemRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new WorkItemRepository(_db);

        var project = new Project { Name = "P" };
        _db.Projects.Add(project);
        _db.SaveChanges();
        _projectId = project.Id;
    }

    [Fact]
    public async Task GetWorkItemsPagedAsync_ReturnsPagedResults()
    {
        _db.WorkItems.AddRange(
            new WorkItem { ProjectId = _projectId, Title = "A", Order = 1 },
            new WorkItem { ProjectId = _projectId, Title = "B", Order = 2 },
            new WorkItem { ProjectId = _projectId, Title = "C", Order = 3 }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetWorkItemsPagedAsync(null, null, null, false, 1, 2, ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, total);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task GetWorkItemsPagedAsync_WithProjectId_Filters()
    {
        _db.WorkItems.AddRange(
            new WorkItem { ProjectId = _projectId, Title = "A", Order = 1 },
            new WorkItem { ProjectId = 999, Title = "B", Order = 1 }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetWorkItemsPagedAsync(_projectId, null, null, false, 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetWorkItemsPagedAsync_WithSearch_Filters()
    {
        _db.WorkItems.AddRange(
            new WorkItem { ProjectId = _projectId, Title = "Fix login bug", Order = 1 },
            new WorkItem { ProjectId = _projectId, Title = "Add feature", Order = 2 }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetWorkItemsPagedAsync(null, "login", null, false, 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetWorkItemsPagedAsync_WithType_Filters()
    {
        _db.WorkItems.AddRange(
            new WorkItem { ProjectId = _projectId, Title = "Bug", Type = WorkItemType.Bug, Order = 1 },
            new WorkItem { ProjectId = _projectId, Title = "Story", Type = WorkItemType.UserStory, Order = 2 }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetWorkItemsPagedAsync(null, null, null, false, 1, 10, type: WorkItemType.Bug, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetWorkItemsPagedAsync_WithStatus_Filters()
    {
        _db.WorkItems.AddRange(
            new WorkItem { ProjectId = _projectId, Title = "Open", Status = WorkItemStatus.New, Order = 1 },
            new WorkItem { ProjectId = _projectId, Title = "Done", Status = WorkItemStatus.Closed, Order = 2 }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetWorkItemsPagedAsync(null, null, null, false, 1, 10, status: WorkItemStatus.Closed, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetWorkItemsPagedAsync_SortByPriority()
    {
        _db.WorkItems.AddRange(
            new WorkItem { ProjectId = _projectId, Title = "Low", Priority = 3, Order = 1 },
            new WorkItem { ProjectId = _projectId, Title = "High", Priority = 1, Order = 2 }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, _) = await _repo.GetWorkItemsPagedAsync(null, null, "priority", false, 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal("High", items[0].Title);
    }

    [Fact]
    public async Task GetWorkItemsPagedAsync_SortByStatus()
    {
        _db.WorkItems.AddRange(
            new WorkItem { ProjectId = _projectId, Title = "A", Status = WorkItemStatus.Closed, Order = 1 },
            new WorkItem { ProjectId = _projectId, Title = "B", Status = WorkItemStatus.New, Order = 2 }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, _) = await _repo.GetWorkItemsPagedAsync(null, null, "status", false, 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task GetWorkItemsPagedAsync_SortByCreatedAtDesc()
    {
        // Save both entities first (SaveChangesAsync stamps all Added entities with the same CreatedAt).
        _db.WorkItems.AddRange(
            new WorkItem { ProjectId = _projectId, Title = "Old", Order = 1 },
            new WorkItem { ProjectId = _projectId, Title = "New", Order = 2 }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Back-date the "Old" item (Modified state does not re-stamp CreatedAt).
        var oldItem = _db.WorkItems.First(w => w.Title == "Old");
        oldItem.CreatedAt = DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, _) = await _repo.GetWorkItemsPagedAsync(null, null, "createdat", true, 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal("New", items[0].Title);
    }

    [Fact]
    public async Task GetWorkItemDetailAsync_Found_IncludesChildren()
    {
        var parent = new WorkItem { ProjectId = _projectId, Title = "Parent", Order = 1 };
        _db.WorkItems.Add(parent);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.WorkItems.Add(new WorkItem { ProjectId = _projectId, Title = "Child", ParentId = parent.Id, Order = 2 });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetWorkItemDetailAsync(parent.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Single(result.Children);
    }

    [Fact]
    public async Task GetWorkItemDetailAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetWorkItemDetailAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindWorkItemAsync_Found()
    {
        var item = new WorkItem { ProjectId = _projectId, Title = "Item", Order = 1 };
        _db.WorkItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindWorkItemAsync(item.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task AddWorkItemAsync_Persists()
    {
        await _repo.AddWorkItemAsync(new WorkItem { ProjectId = _projectId, Title = "New", Order = 1 }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.WorkItems.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveWorkItemAsync_Removes()
    {
        var item = new WorkItem { ProjectId = _projectId, Title = "Del", Order = 1 };
        _db.WorkItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveWorkItemAsync(item, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.WorkItems.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetBoardItemsAsync_ReturnsOrderedByOrder()
    {
        _db.WorkItems.AddRange(
            new WorkItem { ProjectId = _projectId, Title = "Second", Order = 2 },
            new WorkItem { ProjectId = _projectId, Title = "First", Order = 1 }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetBoardItemsAsync(_projectId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal("First", result[0].Title);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.WorkItems.Add(new WorkItem { ProjectId = _projectId, Title = "Pending", Order = 1 });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.WorkItems.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
