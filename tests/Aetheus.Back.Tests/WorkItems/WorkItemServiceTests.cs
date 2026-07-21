// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.WorkItems;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class WorkItemServiceTests
{
    private readonly IWorkItemRepository _repo = Substitute.For<IWorkItemRepository>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly WorkItemService _sut;

    public WorkItemServiceTests()
    {
        _sut = new WorkItemService(_repo, _audit, TimeProvider.System);
    }

    [Fact]
    public async Task MoveWorkItemAsync_NotFound_ReturnsNull()
    {
        _repo.FindWorkItemAsync(99, Arg.Any<CancellationToken>()).Returns((WorkItem?)null);
        Assert.Null(await _sut.MoveWorkItemAsync(99, new MoveWorkItemRequest { Status = WorkItemStatus.Active, Order = 1 }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MoveWorkItemAsync_Found_UpdatesStatusAndOrder()
    {
        var entity = new WorkItem { Id = 1, ProjectId = 1, Title = "t", Status = WorkItemStatus.New, Order = 0, Tags = "[]" };
        _repo.FindWorkItemAsync(1, Arg.Any<CancellationToken>()).Returns(entity);

        var result = await _sut.MoveWorkItemAsync(1, new MoveWorkItemRequest { Status = WorkItemStatus.Active, Order = 3 }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(WorkItemStatus.Active, entity.Status);
        Assert.Equal(3, entity.Order);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync("Moved", "WorkItem", 1, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetWorkItemsAsync_ReturnsPaginatedResult()
    {
        var item = new WorkItem
        {
            Id = 1,
            ProjectId = 1,
            Title = "Fix bug",
            Type = WorkItemType.Bug,
            Status = WorkItemStatus.New,
            Tags = "[]"
        };
        _repo.GetWorkItemsPagedAsync(
                null, null, null, false, 1, 10, null, null, null, Arg.Any<CancellationToken>())
            .Returns((new List<WorkItem> { item }, 1));

        var result = await _sut.GetWorkItemsAsync(new WorkItemPaginationRequest { Page = 1, PageSize = 10 }, ct: TestContext.Current.CancellationToken);

        Assert.Single(result.Items);
        Assert.Equal("Fix bug", result.Items[0].Title);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task GetWorkItemDetailAsync_Found_ReturnsDtoWithChildren()
    {
        _repo.GetWorkItemDetailAsync(1, Arg.Any<CancellationToken>())
            .Returns(new WorkItem
            {
                Id = 1,
                ProjectId = 1,
                Title = "Epic",
                Type = WorkItemType.Epic,
                Status = WorkItemStatus.Active,
                Tags = "[]",
                Children = [new WorkItem
                {
                    Id = 2, ProjectId = 1, Title = "Child Task",
                    Type = WorkItemType.Task, Status = WorkItemStatus.New, Tags = "[\"backend\"]",
                    Children = []
                }]
            });

        var result = await _sut.GetWorkItemDetailAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Epic", result.Title);
        Assert.Single(result.Children);
        Assert.Equal("Child Task", result.Children[0].Title);
    }

    [Fact]
    public async Task GetWorkItemDetailAsync_NotFound_ReturnsNull()
    {
        _repo.GetWorkItemDetailAsync(99, Arg.Any<CancellationToken>())
            .Returns((WorkItem?)null);

        Assert.Null(await _sut.GetWorkItemDetailAsync(99, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateWorkItemAsync_CreatesAndReturnsDto()
    {
        _repo.AddWorkItemAsync(Arg.Any<WorkItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _sut.CreateWorkItemAsync(new CreateWorkItemRequest
        {
            ProjectId = 1,
            Title = "New feature",
            Type = WorkItemType.Feature,
            Tags = ["frontend", "urgent"]
        }, ct: TestContext.Current.CancellationToken);

        Assert.Equal("New feature", result.Title);
        Assert.Equal(WorkItemType.Feature, result.Type);
        await _repo.Received(1).AddWorkItemAsync(Arg.Any<WorkItem>(), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync("Created", "WorkItem", Arg.Any<int>(), "Feature: New feature", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateWorkItemAsync_NotFound_ReturnsNull()
    {
        _repo.FindWorkItemAsync(99, Arg.Any<CancellationToken>())
            .Returns((WorkItem?)null);

        Assert.Null(await _sut.UpdateWorkItemAsync(99, new UpdateWorkItemRequest { Title = "x" }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateWorkItemAsync_Found_UpdatesAndReturns()
    {
        _repo.FindWorkItemAsync(1, Arg.Any<CancellationToken>())
            .Returns(new WorkItem
            {
                Id = 1,
                ProjectId = 1,
                Title = "old",
                Type = WorkItemType.Task,
                Status = WorkItemStatus.New,
                Tags = "[]"
            });
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _sut.UpdateWorkItemAsync(1, new UpdateWorkItemRequest
        {
            Title = "updated",
            Type = WorkItemType.Bug,
            Status = WorkItemStatus.Active,
            Tags = ["fix"]
        }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("updated", result.Title);
        Assert.Equal(WorkItemType.Bug, result.Type);
        await _audit.Received(1).LogAsync("Updated", "WorkItem", 1, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteWorkItemAsync_NotFound_ReturnsFalse()
    {
        _repo.FindWorkItemAsync(99, Arg.Any<CancellationToken>())
            .Returns((WorkItem?)null);

        Assert.False(await _sut.DeleteWorkItemAsync(99, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteWorkItemAsync_Found_DeletesAndReturnsTrue()
    {
        _repo.FindWorkItemAsync(1, Arg.Any<CancellationToken>())
            .Returns(new WorkItem { Id = 1, Title = "done", ProjectId = 1, Tags = "[]" });
        _repo.RemoveWorkItemAsync(Arg.Any<WorkItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        Assert.True(await _sut.DeleteWorkItemAsync(1, ct: TestContext.Current.CancellationToken));
        await _audit.Received(1).LogAsync("Deleted", "WorkItem", 1, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBoardAsync_GroupsByStatus()
    {
        _repo.GetBoardItemsAsync(1, Arg.Any<CancellationToken>())
            .Returns([
                new WorkItem { Id = 1, ProjectId = 1, Title = "A", Status = WorkItemStatus.New, Tags = "[]" },
                new WorkItem { Id = 2, ProjectId = 1, Title = "B", Status = WorkItemStatus.Active, Tags = "[]" },
                new WorkItem { Id = 3, ProjectId = 1, Title = "C", Status = WorkItemStatus.New, Tags = "[]" }
            ]);

        var result = await _sut.GetBoardAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotEmpty(result);
        var newColumn = result.FirstOrDefault(c => c.Status == WorkItemStatus.New);
        Assert.NotNull(newColumn);
        Assert.Equal(2, newColumn.Items.Count);
    }
}
