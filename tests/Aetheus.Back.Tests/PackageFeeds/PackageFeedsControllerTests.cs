// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.PackageFeeds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class PackageFeedsControllerTests
{
    private readonly IPackageFeedService _serviceMock = Substitute.For<IPackageFeedService>();
    private readonly PackageFeedsController _sut;

    public PackageFeedsControllerTests()
    {
        _sut = new PackageFeedsController(_serviceMock);
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
    public async Task GetFeeds_ReturnsOk()
    {
        _serviceMock.GetFeedsAsync(null, Arg.Any<PaginationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedResult<PackageFeedDto>
            {
                Items = [new PackageFeedDto { Id = 1, Name = "NuGet" }],
                TotalCount = 1,
                Page = 1,
                PageSize = 25
            });

        var result = await _sut.GetFeeds(null, new PaginationRequest(), TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(Assert.IsType<PaginatedResult<PackageFeedDto>>(ok.Value).Items);
    }

    [Fact]
    public async Task GetFeed_Found_ReturnsOk()
    {
        _serviceMock.GetFeedDetailAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PackageFeedDetailDto { Id = 1, Name = "NuGet" });

        var result = await _sut.GetFeed(1, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetFeed_NotFound_ReturnsNotFound()
    {
        _serviceMock.GetFeedDetailAsync(999, Arg.Any<CancellationToken>())
            .Returns((PackageFeedDetailDto?)null);

        var result = await _sut.GetFeed(999, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task CreateFeed_ReturnsCreated()
    {
        _serviceMock.CreateFeedAsync(Arg.Any<CreatePackageFeedRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PackageFeedDto { Id = 1, Name = "New" });

        var result = await _sut.CreateFeed(new CreatePackageFeedRequest { Name = "New", UpstreamUrl = "https://nuget.org" }, TestContext.Current.CancellationToken);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task UpdateFeed_Found_ReturnsOk()
    {
        _serviceMock.UpdateFeedAsync(1, Arg.Any<UpdatePackageFeedRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PackageFeedDto { Id = 1, Name = "Updated" });

        var result = await _sut.UpdateFeed(1, new UpdatePackageFeedRequest { Name = "Updated", UpstreamUrl = "https://nuget.org" }, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateFeed_NotFound_ReturnsNotFound()
    {
        _serviceMock.UpdateFeedAsync(999, Arg.Any<UpdatePackageFeedRequest>(), Arg.Any<CancellationToken>())
            .Returns((PackageFeedDto?)null);

        var result = await _sut.UpdateFeed(999, new UpdatePackageFeedRequest { Name = "X", UpstreamUrl = "https://x.com" }, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task DeleteFeed_Deleted_ReturnsNoContent()
    {
        _serviceMock.DeleteFeedAsync(1, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.DeleteFeed(1, TestContext.Current.CancellationToken);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteFeed_NotFound_ReturnsNotFound()
    {
        _serviceMock.DeleteFeedAsync(999, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.DeleteFeed(999, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task AddPackage_Found_ReturnsOk()
    {
        _serviceMock.AddPackageAsync(1, Arg.Any<AddPackageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PackageEntryDto { Id = 5, PackageFeedId = 1, Name = "Newtonsoft.Json" });

        var result = await _sut.AddPackage(1, new AddPackageRequest { Name = "Newtonsoft.Json" }, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task AddPackage_FeedUnknown_ReturnsNotFound()
    {
        _serviceMock.AddPackageAsync(1, Arg.Any<AddPackageRequest>(), Arg.Any<CancellationToken>())
            .Returns((PackageEntryDto?)null);

        var result = await _sut.AddPackage(1, new AddPackageRequest { Name = "x" }, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task SyncFeed_Found_ReturnsOk()
    {
        _serviceMock.SyncFeedAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PackageFeedSyncResultDto { Synced = 2 });

        var result = await _sut.SyncFeed(1, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task RemovePackage_NotFound_ReturnsNotFound()
    {
        _serviceMock.RemovePackageAsync(1, 5, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.RemovePackage(1, 5, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
    }
}
