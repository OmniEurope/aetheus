// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Environments;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using NSubstitute;
using Environment = Aetheus.Back.Data.Entities.Environment;

namespace Aetheus.Back.Tests;

public class EnvironmentServiceTests
{
    private readonly IEnvironmentRepository _repo = Substitute.For<IEnvironmentRepository>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly EnvironmentService _sut;

    public EnvironmentServiceTests()
    {
        _sut = new EnvironmentService(_repo,
            _audit, Substitute.For<IEntityChangeNotifier>(),
            Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>());
    }

    [Fact]
    public async Task GetEnvironmentsAsync_ReturnsPaginatedResult()
    {
        var env = new Environment { Id = 1, Name = "prod", Servers = [] };
        _repo.GetEnvironmentsPagedAsync(null, null, 1, 10, Arg.Any<List<int>?>(), Arg.Any<CancellationToken>())
            .Returns((new List<Environment> { env }, 1));

        var result = await _sut.GetEnvironmentsAsync(null, new PaginationRequest { Page = 1, PageSize = 10 }, ct: TestContext.Current.CancellationToken);

        Assert.Single(result.Items);
        Assert.Equal("prod", result.Items[0].Name);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task GetEnvironmentAsync_Found_ReturnsDto()
    {
        _repo.GetEnvironmentWithServersAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Environment
            {
                Id = 1,
                Name = "staging",
                Servers = [new EnvironmentServer { ServerId = 10, Server = new Server { Id = 10, Name = "web-01", Status = ServerStatus.Online } }]
            });

        var result = await _sut.GetEnvironmentAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("staging", result.Name);
        Assert.Single(result.Servers);
        Assert.Equal("web-01", result.Servers[0].ServerName);
    }

    [Fact]
    public async Task GetEnvironmentAsync_NotFound_ReturnsNull()
    {
        _repo.GetEnvironmentWithServersAsync(99, Arg.Any<CancellationToken>())
            .Returns((Environment?)null);

        var result = await _sut.GetEnvironmentAsync(99, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateEnvironmentAsync_CreatesAndReturnsDto()
    {
        _repo.AddEnvironmentAsync(Arg.Any<Environment>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repo.GetEnvironmentWithServersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new Environment { Id = 1, Name = "dev", Servers = [] });

        var result = await _sut.CreateEnvironmentAsync(new CreateEnvironmentRequest
        {
            Name = "dev",
            ServerIds = []
        }, ct: TestContext.Current.CancellationToken);

        Assert.Equal("dev", result.Name);
        await _repo.Received(1).AddEnvironmentAsync(Arg.Any<Environment>(), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync("Created", "Environment", Arg.Any<int>(), "dev", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateEnvironmentAsync_NotFound_ReturnsNull()
    {
        _repo.FindEnvironmentAsync(99, Arg.Any<CancellationToken>())
            .Returns((Environment?)null);

        var result = await _sut.UpdateEnvironmentAsync(99, new UpdateEnvironmentRequest { Name = "x", ServerIds = [] }, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateEnvironmentAsync_Found_UpdatesAndReturns()
    {
        var env = new Environment { Id = 1, Name = "old", Servers = [] };
        _repo.FindEnvironmentAsync(1, Arg.Any<CancellationToken>())
            .Returns(env);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repo.GetEnvironmentWithServersAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Environment { Id = 1, Name = "new", Servers = [] });

        var result = await _sut.UpdateEnvironmentAsync(1, new UpdateEnvironmentRequest { Name = "new", ServerIds = [] }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        await _audit.Received(1).LogAsync("Updated", "Environment", 1, "new", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteEnvironmentAsync_NotFound_ReturnsFalse()
    {
        _repo.FindEnvironmentAsync(99, Arg.Any<CancellationToken>())
            .Returns((Environment?)null);

        var result = await _sut.DeleteEnvironmentAsync(99, ct: TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteEnvironmentAsync_Found_DeletesAndReturnsTrue()
    {
        _repo.FindEnvironmentAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Environment { Id = 1, Name = "prod" });
        _repo.RemoveEnvironmentAsync(Arg.Any<Environment>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _sut.DeleteEnvironmentAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.True(result);
        await _repo.Received(1).RemoveEnvironmentAsync(Arg.Any<Environment>(), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync("Deleted", "Environment", 1, "prod", Arg.Any<CancellationToken>());
    }
}
