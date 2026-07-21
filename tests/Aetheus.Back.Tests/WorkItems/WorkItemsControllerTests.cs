// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.WorkItems;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class WorkItemsControllerTests
{
    private readonly IWorkItemService _serviceMock = Substitute.For<IWorkItemService>();
    private readonly IResourceAuthorizationService _authzMock = Substitute.For<IResourceAuthorizationService>();
    private readonly WorkItemsController _sut;

    public WorkItemsControllerTests()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<ResourceType>(), Arg.Any<int?>(), Arg.Any<Permission>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.GetProjectIdForItemAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _sut = new WorkItemsController(_serviceMock, _authzMock);
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "1")], "test"))
            }
        };
    }

    [Fact]
    public async Task GetWorkItems_NoProjectFilter_ReturnsOk()
    {
        _serviceMock.GetWorkItemsAsync(Arg.Any<WorkItemPaginationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedResult<WorkItemDto> { Items = [new WorkItemDto { Id = 1, Title = "Task1" }], TotalCount = 1 });

        var result = await _sut.GetWorkItems(new WorkItemPaginationRequest { ProjectId = 1 }, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetWorkItems_WithProjectFilter_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Project, 5, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.GetWorkItems(new WorkItemPaginationRequest { ProjectId = 5 }, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetWorkItem_Found_ReturnsOk()
    {
        _serviceMock.GetWorkItemDetailAsync(1, Arg.Any<CancellationToken>())
            .Returns(new WorkItemDetailDto { Id = 1, Title = "Task1" });

        var result = await _sut.GetWorkItem(1, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetWorkItem_NotFound_ReturnsNotFound()
    {
        _serviceMock.GetWorkItemDetailAsync(999, Arg.Any<CancellationToken>())
            .Returns((WorkItemDetailDto?)null);

        var result = await _sut.GetWorkItem(999, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task CreateWorkItem_Authorized_ReturnsCreated()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Project, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.CreateWorkItemAsync(Arg.Any<CreateWorkItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(new WorkItemDto { Id = 1, Title = "New", ProjectId = 1 });

        var result = await _sut.CreateWorkItem(new CreateWorkItemRequest { ProjectId = 1, Title = "New" }, TestContext.Current.CancellationToken);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task CreateWorkItem_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Project, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.CreateWorkItem(new CreateWorkItemRequest { ProjectId = 1, Title = "X" }, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task UpdateWorkItem_Found_ReturnsOk()
    {
        _serviceMock.UpdateWorkItemAsync(1, Arg.Any<UpdateWorkItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(new WorkItemDto { Id = 1, Title = "Updated" });

        var result = await _sut.UpdateWorkItem(1, new UpdateWorkItemRequest { Title = "Updated" }, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateWorkItem_NotFound_ReturnsNotFound()
    {
        _serviceMock.UpdateWorkItemAsync(999, Arg.Any<UpdateWorkItemRequest>(), Arg.Any<CancellationToken>())
            .Returns((WorkItemDto?)null);

        var result = await _sut.UpdateWorkItem(999, new UpdateWorkItemRequest { Title = "X" }, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task DeleteWorkItem_Deleted_ReturnsNoContent()
    {
        _serviceMock.DeleteWorkItemAsync(1, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.DeleteWorkItem(1, TestContext.Current.CancellationToken);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteWorkItem_NotFound_ReturnsNotFound()
    {
        _serviceMock.DeleteWorkItemAsync(999, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.DeleteWorkItem(999, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetBoard_Authorized_ReturnsOk()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Project, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(true);
        _serviceMock.GetBoardAsync(1, Arg.Any<CancellationToken>())
            .Returns([new WorkItemBoardColumn { Status = WorkItemStatus.New }]);

        var result = await _sut.GetBoard(1, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetBoard_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Project, 1, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.GetBoard(1, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task MoveWorkItem_Authorized_ReturnsOk()
    {
        _serviceMock.MoveWorkItemAsync(1, Arg.Any<MoveWorkItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(new WorkItemDto { Id = 1, Status = WorkItemStatus.Active });

        var result = await _sut.MoveWorkItem(1, new MoveWorkItemRequest { Status = WorkItemStatus.Active, Order = 1 }, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task MoveWorkItem_Forbidden_ReturnsForbid()
    {
        _authzMock.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Project, 1, Permission.Write, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.MoveWorkItem(1, new MoveWorkItemRequest { Status = WorkItemStatus.Active, Order = 1 }, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task MoveWorkItem_ItemUnknown_ReturnsNotFound()
    {
        _serviceMock.GetProjectIdForItemAsync(42, Arg.Any<CancellationToken>()).Returns((int?)null);

        var result = await _sut.MoveWorkItem(42, new MoveWorkItemRequest { Status = WorkItemStatus.Active, Order = 1 }, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
