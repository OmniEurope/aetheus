// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.ServerApps;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class ServerAppServiceTests
{
    private readonly IServerAppRepository _repoMock = Substitute.For<IServerAppRepository>();
    private readonly IAuditService _auditMock = Substitute.For<IAuditService>();
    private readonly ServerAppService _sut;

    public ServerAppServiceTests()
    {
        _sut = new ServerAppService(_repoMock, _auditMock, TimeProvider.System);
    }

    [Fact]
    public async Task GetByServerIdAsync_ReturnsMappedApps()
    {
        _repoMock.GetByServerIdAsync(1, Arg.Any<CancellationToken>())
            .Returns([new ServerApp
            {
                Id = 10, ServerId = 1, Name = "nginx", Version = "1.25",
                Type = "WebServer", Port = 80, Path = "/usr/sbin/nginx",
                Source = "apt", Status = ServerAppStatus.Running
            }]);

        var result = await _sut.GetByServerIdAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("nginx", result[0].Name);
        Assert.Equal(80, result[0].Port);
    }

    [Fact]
    public async Task GetByIdAsync_Found_ReturnsDto()
    {
        _repoMock.GetByIdAsync(10, Arg.Any<CancellationToken>())
            .Returns(new ServerApp { Id = 10, ServerId = 1, Name = "nginx", Version = "1.25", Type = "WebServer" });

        var result = await _sut.GetByIdAsync(1, 10, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("nginx", result.Name);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        _repoMock.GetByIdAsync(999, Arg.Any<CancellationToken>()).Returns((ServerApp?)null);

        var result = await _sut.GetByIdAsync(1, 999, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_CreatesAppAndAudits()
    {
        var request = new CreateServerAppRequest
        {
            Name = "myapp",
            Version = "2.0",
            Type = "Application",
            Port = 8080,
            Path = "/opt/myapp",
            Source = "docker"
        };

        _repoMock.AddAsync(Arg.Any<ServerApp>(), Arg.Any<CancellationToken>())
            .Returns(new ServerApp { Id = 1, Name = "myapp" });

        var result = await _sut.CreateAsync(1, request, ct: TestContext.Current.CancellationToken);

        Assert.Equal("myapp", result.Name);
        Assert.Equal(8080, result.Port);
        await _auditMock.Received(1).LogAsync("Created", "ServerApp", Arg.Any<int>(), "myapp", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_Found_UpdatesAndAudits()
    {
        var entity = new ServerApp { Id = 10, ServerId = 1, Name = "old", Version = "1.0", Type = "Application" };
        _repoMock.GetByIdAsync(10, Arg.Any<CancellationToken>()).Returns(entity);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var request = new UpdateServerAppRequest
        {
            Name = "updated",
            Version = "2.0",
            Status = ServerAppStatus.Stopped,
            Port = 9090,
            Path = "/new"
        };

        var result = await _sut.UpdateAsync(1, 10, request, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("updated", entity.Name);
        Assert.Equal("2.0", entity.Version);
        await _auditMock.Received(1).LogAsync("Updated", "ServerApp", 10, "updated", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsNull()
    {
        _repoMock.GetByIdAsync(999, Arg.Any<CancellationToken>()).Returns((ServerApp?)null);

        var result = await _sut.UpdateAsync(1, 999, new UpdateServerAppRequest { Name = "x" }, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_Found_DeletesAndAudits()
    {
        var entity = new ServerApp { Id = 10, ServerId = 1, Name = "myapp", Type = "Application" };
        _repoMock.GetByIdAsync(10, Arg.Any<CancellationToken>()).Returns(entity);

        var result = await _sut.DeleteAsync(1, 10, ct: TestContext.Current.CancellationToken);

        Assert.True(result);
        await _repoMock.Received(1).RemoveAsync(entity, Arg.Any<CancellationToken>());
        await _auditMock.Received(1).LogAsync("Deleted", "ServerApp", 10, "myapp", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ReturnsFalse()
    {
        _repoMock.GetByIdAsync(999, Arg.Any<CancellationToken>()).Returns((ServerApp?)null);

        var result = await _sut.DeleteAsync(1, 999, ct: TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task GetByServerIdAsync_EmptyList_ReturnsEmpty()
    {
        _repoMock.GetByServerIdAsync(1, Arg.Any<CancellationToken>()).Returns([]);

        var result = await _sut.GetByServerIdAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }
}
