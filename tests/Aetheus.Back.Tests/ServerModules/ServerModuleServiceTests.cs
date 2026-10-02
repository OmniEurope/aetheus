// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.ServerModules;
using Aetheus.Back.Data.Entities;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class ServerModuleServiceTests
{
    private readonly IServerModuleRepository _repoMock = Substitute.For<IServerModuleRepository>();
    private readonly IAuditService _auditMock = Substitute.For<IAuditService>();
    private readonly ServerModuleService _sut;

    public ServerModuleServiceTests()
    {
        _sut = new ServerModuleService(_repoMock, _auditMock, TimeProvider.System);
    }

    [Fact]
    public async Task GetByServerIdAsync_ReturnsMappedModules()
    {
        _repoMock.GetByServerIdAsync(1, Arg.Any<CancellationToken>())
            .Returns([new ServerModule
            {
                Id = 10, ServerId = 1, Name = "Docker", Type = ServerModuleType.Docker,
                Version = "24.0", Status = ServerModuleStatus.Active
            }]);

        var result = await _sut.GetByServerIdAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("Docker", result[0].Name);
        Assert.Equal(ServerModuleType.Docker, result[0].Type);
    }

    [Fact]
    public async Task GetByIdAsync_Found_ReturnsDto()
    {
        _repoMock.GetByIdAsync(10, Arg.Any<CancellationToken>())
            .Returns(new ServerModule { Id = 10, ServerId = 1, Name = "Docker", Type = ServerModuleType.Docker });

        var result = await _sut.GetByIdAsync(1, 10, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Docker", result.Name);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        _repoMock.GetByIdAsync(999, Arg.Any<CancellationToken>()).Returns((ServerModule?)null);

        var result = await _sut.GetByIdAsync(1, 999, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_CreatesModuleAndAudits()
    {
        var request = new CreateServerModuleRequest
        {
            Name = "Apache",
            Type = ServerModuleType.Apache,
            Version = "2.4",
            Configuration = "{\"key\":\"value\"}"
        };

        _repoMock.AddAsync(Arg.Any<ServerModule>(), Arg.Any<CancellationToken>())
            .Returns(new ServerModule { Id = 1, Name = "Apache" });

        var result = await _sut.CreateAsync(1, request, ct: TestContext.Current.CancellationToken);

        Assert.Equal("Apache", result.Name);
        await _repoMock.Received(1).AddAsync(Arg.Is<ServerModule>(m => m.Name == "Apache" && m.Configuration == "{\"key\":\"value\"}"), Arg.Any<CancellationToken>());
        await _auditMock.Received(1).LogAsync("Created", "ServerModule", Arg.Any<int>(), "Apache", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_NullConfiguration_DefaultsToEmptyJson()
    {
        var request = new CreateServerModuleRequest { Name = "Test", Type = ServerModuleType.Docker, Version = "1.0", Configuration = null };

        _repoMock.AddAsync(Arg.Any<ServerModule>(), Arg.Any<CancellationToken>())
            .Returns(new ServerModule { Id = 1 });

        await _sut.CreateAsync(1, request, ct: TestContext.Current.CancellationToken);

        await _repoMock.Received(1).AddAsync(Arg.Is<ServerModule>(m => m.Configuration == "{}"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_Found_UpdatesAndAudits()
    {
        var entity = new ServerModule { Id = 10, ServerId = 1, Name = "Old", Type = ServerModuleType.Docker, Version = "1.0" };
        _repoMock.GetByIdAsync(10, Arg.Any<CancellationToken>()).Returns(entity);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var request = new UpdateServerModuleRequest
        {
            Name = "Updated",
            Status = ServerModuleStatus.Inactive,
            Version = "2.0",
            Configuration = "{}"
        };

        var result = await _sut.UpdateAsync(1, 10, request, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Updated", entity.Name);
        Assert.Equal(ServerModuleStatus.Inactive, entity.Status);
        await _auditMock.Received(1).LogAsync("Updated", "ServerModule", 10, "Updated", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_NullConfiguration_DefaultsToEmptyJson()
    {
        var entity = new ServerModule { Id = 10, ServerId = 1, Name = "Mod", Type = ServerModuleType.Docker, Configuration = "{\"old\":true}" };
        _repoMock.GetByIdAsync(10, Arg.Any<CancellationToken>()).Returns(entity);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _sut.UpdateAsync(1, 10, new UpdateServerModuleRequest { Name = "Mod", Configuration = null }, ct: TestContext.Current.CancellationToken);

        Assert.Equal("{}", entity.Configuration);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsNull()
    {
        _repoMock.GetByIdAsync(999, Arg.Any<CancellationToken>()).Returns((ServerModule?)null);

        var result = await _sut.UpdateAsync(1, 999, new UpdateServerModuleRequest { Name = "x" }, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_Found_DeletesAndAudits()
    {
        var entity = new ServerModule { Id = 10, ServerId = 1, Name = "Docker", Type = ServerModuleType.Docker };
        _repoMock.GetByIdAsync(10, Arg.Any<CancellationToken>()).Returns(entity);

        var result = await _sut.DeleteAsync(1, 10, ct: TestContext.Current.CancellationToken);

        Assert.True(result);
        await _repoMock.Received(1).RemoveAsync(entity, Arg.Any<CancellationToken>());
        await _auditMock.Received(1).LogAsync("Deleted", "ServerModule", 10, "Docker", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ReturnsFalse()
    {
        _repoMock.GetByIdAsync(999, Arg.Any<CancellationToken>()).Returns((ServerModule?)null);

        var result = await _sut.DeleteAsync(1, 999, ct: TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    // serverId scoping: a moduleId that exists but belongs to another server must be treated as absent
    // (defends against reading/mutating a module across the server boundary via a guessed id).

    [Fact]
    public async Task GetByIdAsync_WrongServerId_ReturnsNull()
    {
        _repoMock.GetByIdAsync(10, Arg.Any<CancellationToken>())
            .Returns(new ServerModule { Id = 10, ServerId = 1, Name = "Docker", Type = ServerModuleType.Docker });

        var result = await _sut.GetByIdAsync(2, 10, ct: TestContext.Current.CancellationToken); // module belongs to server 1, requested under server 2

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_WrongServerId_ReturnsNullAndDoesNotSave()
    {
        _repoMock.GetByIdAsync(10, Arg.Any<CancellationToken>())
            .Returns(new ServerModule { Id = 10, ServerId = 1, Name = "Docker", Type = ServerModuleType.Docker });

        var result = await _sut.UpdateAsync(2, 10, new UpdateServerModuleRequest { Name = "x" }, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
        await _repoMock.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_WrongServerId_ReturnsFalseAndDoesNotRemove()
    {
        _repoMock.GetByIdAsync(10, Arg.Any<CancellationToken>())
            .Returns(new ServerModule { Id = 10, ServerId = 1, Name = "Docker", Type = ServerModuleType.Docker });

        var result = await _sut.DeleteAsync(2, 10, ct: TestContext.Current.CancellationToken);

        Assert.False(result);
        await _repoMock.DidNotReceive().RemoveAsync(Arg.Any<ServerModule>(), Arg.Any<CancellationToken>());
    }
}
