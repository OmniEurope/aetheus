// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Organizations;
using Aetheus.Back.Components.Projects;
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Services;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class ProjectServiceProjectServerTests
{
    private readonly IProjectRepository _repo = Substitute.For<IProjectRepository>();
    private readonly ProjectService _sut;

    public ProjectServiceProjectServerTests()
    {
        var orgService = Substitute.For<IOrganizationService>();
        orgService.GetDefaultOrganizationIdAsync(Arg.Any<CancellationToken>()).Returns(1);
        _sut = new ProjectService(_repo, Substitute.For<IAuditService>(), Substitute.For<IEntityChangeNotifier>(),
            orgService, Substitute.For<IServerLifecycleService>(), TimeProvider.System, Substitute.For<IMemoryCache>(), Substitute.For<Aetheus.Back.Components.PortRegistry.IPortRegistryService>(),
            Substitute.For<Aetheus.Back.Components.Notifications.IUserNotificationService>());
    }

    private static ProjectServer Entity(int id = 1) =>
        new() { Id = id, ProjectId = 1, Type = ProjectServerType.ExternalHost, DisplayName = "web", Host = "web.local" };

    // --- CreateProjectServerAsync ---

    [Fact]
    public async Task Create_ProjectNotFound_Throws()
    {
        _repo.FindProjectAsync(9, TestContext.Current.CancellationToken).Returns((Project?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.CreateProjectServerAsync(9, new CreateProjectServerRequest { Type = ProjectServerType.ExternalHost, DisplayName = "x", Host = "h" }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_AgentServerWithoutServerId_ThrowsBadRequest()
    {
        _repo.FindProjectAsync(1, TestContext.Current.CancellationToken).Returns(new Project { Id = 1, Name = "P", Pipelines = [] });

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.CreateProjectServerAsync(1, new CreateProjectServerRequest { Type = ProjectServerType.AgentServer, ServerId = null, DisplayName = "x", Host = "h" }, ct: TestContext.Current.CancellationToken));
    }

    // --- UpdateProjectServerAsync ---

    [Fact]
    public async Task Update_NotFound_ReturnsNull()
    {
        _repo.GetProjectServerAsync(1, 9, TestContext.Current.CancellationToken).Returns((ProjectServer?)null);

        Assert.Null(await _sut.UpdateProjectServerAsync(1, 9, new UpdateProjectServerRequest { DisplayName = "x", Host = "h" }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_Found_UpdatesFields()
    {
        var entity = Entity();
        _repo.GetProjectServerAsync(1, 1, TestContext.Current.CancellationToken).Returns(entity);

        var result = await _sut.UpdateProjectServerAsync(1, 1, new UpdateProjectServerRequest { DisplayName = "renamed", Host = "new.local", Port = 22 }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("renamed", entity.DisplayName);
        Assert.Equal("new.local", entity.Host);
    }

    // --- DeleteProjectServerAsync ---

    [Fact]
    public async Task Delete_NotFound_ReturnsFalse()
    {
        _repo.GetProjectServerAsync(1, 9, TestContext.Current.CancellationToken).Returns((ProjectServer?)null);

        Assert.False(await _sut.DeleteProjectServerAsync(1, 9, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_Found_ReturnsTrue()
    {
        _repo.GetProjectServerAsync(1, 1, TestContext.Current.CancellationToken).Returns(Entity());

        Assert.True(await _sut.DeleteProjectServerAsync(1, 1, ct: TestContext.Current.CancellationToken));
    }
}
