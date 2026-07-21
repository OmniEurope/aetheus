// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class AuditServiceTests
{
    private readonly IAuditRepository _repoMock = Substitute.For<IAuditRepository>();
    private readonly IHttpContextAccessor _httpMock = Substitute.For<IHttpContextAccessor>();
    private readonly AuditService _sut;

    public AuditServiceTests()
    {
        var context = new DefaultHttpContext();
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "testuser")], "test"));
        _httpMock.HttpContext.Returns(context);
        _sut = new AuditService(_repoMock, _httpMock, Substitute.For<IAuditChainService>(), Substitute.For<IMemoryCache>(), TimeProvider.System);
    }

    [Fact]
    public async Task GetLogsPagedAsync_PassesFiltersThroughToRepository()
    {
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        _repoMock
            .GetPagedAsync(0, 50, "q", "Created", "Server", null, from, to, TestContext.Current.CancellationToken)
            .Returns([new AuditLog { Id = 1, Username = "admin", Action = "Created", EntityType = "Server", Timestamp = DateTime.UtcNow }]);
        _repoMock.CountAsync("q", "Created", "Server", null, from, to, TestContext.Current.CancellationToken).Returns(1);

        var result = await _sut.GetLogsPagedAsync(1, 50, "q", "Created", "Server", null, from, to, ct: TestContext.Current.CancellationToken);

        Assert.Single(result.Items);
        Assert.Equal(1, result.TotalCount);
        await _repoMock.Received(1).GetPagedAsync(0, 50, "q", "Created", "Server", null, from, to, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetLogsPagedAsync_ClampsPageSize()
    {
        _repoMock.GetPagedAsync(0, 200, null, null, null, null, null, null, TestContext.Current.CancellationToken).Returns([]);
        _repoMock.CountAsync(null, null, null, null, null, null, TestContext.Current.CancellationToken).Returns(0);

        var result = await _sut.GetLogsPagedAsync(1, 999, ct: TestContext.Current.CancellationToken);

        Assert.Equal(200, result.PageSize);
        await _repoMock.Received(1).GetPagedAsync(0, 200, null, null, null, null, null, null, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetDistinctActionsAsync_DelegatesToRepository()
    {
        _repoMock.GetDistinctActionsAsync(TestContext.Current.CancellationToken).Returns(["Created", "Deleted"]);

        var result = await _sut.GetDistinctActionsAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetDistinctEntityTypesAsync_DelegatesToRepository()
    {
        _repoMock.GetDistinctEntityTypesAsync(TestContext.Current.CancellationToken).Returns(["Server", "Task"]);

        var result = await _sut.GetDistinctEntityTypesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task LogAsync_UsesHttpContextUsername()
    {
        await _sut.LogAsync("Created", "Server", 1, "test details", ct: TestContext.Current.CancellationToken);

        await _repoMock.Received(1).AddAsync(
            Arg.Is<AuditLog>(l => l.Username == "testuser" && l.Action == "Created" && l.EntityType == "Server"),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetLogsPagedAsync_MapsEntityCorrectly()
    {
        _repoMock
            .GetPagedAsync(0, 50, null, null, null, null, null, null, TestContext.Current.CancellationToken)
            .Returns([new AuditLog { Id = 42, Username = "admin", Action = "Updated", EntityType = "Pipeline", EntityId = 7, Details = "detail", Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }]);
        _repoMock.CountAsync(null, null, null, null, null, null, TestContext.Current.CancellationToken).Returns(1);

        var result = await _sut.GetLogsPagedAsync(1, 50, ct: TestContext.Current.CancellationToken);
        var dto = Assert.Single(result.Items);
        Assert.Equal(42, dto.Id);
        Assert.Equal("admin", dto.Username);
        Assert.Equal("Updated", dto.Action);
        Assert.Equal("Pipeline", dto.EntityType);
        Assert.Equal(7, dto.EntityId);
        Assert.Equal("detail", dto.Details);
    }
}
