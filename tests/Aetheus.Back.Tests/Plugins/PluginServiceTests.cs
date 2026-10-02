// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Organizations;
using Aetheus.Back.Components.Plugins;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class PluginServiceTests
{
    private readonly IPluginRepository _repoMock = Substitute.For<IPluginRepository>();
    private readonly IAuditService _auditMock = Substitute.For<IAuditService>();
    private readonly IOrganizationRepository _orgRepoMock = Substitute.For<IOrganizationRepository>();
    private readonly PluginService _sut;

    public PluginServiceTests()
    {
        _auditMock.LogAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _orgRepoMock.GetDefaultOrganizationIdAsync(Arg.Any<CancellationToken>())
            .Returns(1);
        _sut = new PluginService(_repoMock, _auditMock, _orgRepoMock, TimeProvider.System, Substitute.For<IAdminChangeNotifier>());
    }

    [Fact]
    public async Task GetPluginsAsync_ReturnsMappedList()
    {
        _repoMock.GetAllAsync(TestContext.Current.CancellationToken)
            .Returns([new PluginRegistration { Id = 1, Name = "GitSync", Version = "1.0" }]);

        var result = await _sut.GetPluginsAsync(ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("GitSync", result[0].Name);
    }

    [Fact]
    public async Task GetPluginAsync_NotFound_ReturnsNull()
    {
        _repoMock.FindAsync(99, TestContext.Current.CancellationToken).Returns((PluginRegistration?)null);

        Assert.Null(await _sut.GetPluginAsync(99, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetPluginAsync_Found_ReturnsDto()
    {
        _repoMock.FindAsync(1, TestContext.Current.CancellationToken)
            .Returns(new PluginRegistration { Id = 1, Name = "DockerHelper", Version = "2.0", Type = PluginType.Executor });

        var result = await _sut.GetPluginAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(PluginType.Executor, result.Type);
    }

    [Fact]
    public async Task RegisterPluginAsync_CreatesAndAudits()
    {
        _repoMock.AddAsync(Arg.Any<PluginRegistration>(), TestContext.Current.CancellationToken).Returns(Task.CompletedTask);

        var request = new RegisterPluginRequest { Name = "Monitor", Version = "1.0", Type = PluginType.Notifier };
        var result = await _sut.RegisterPluginAsync(request, ct: TestContext.Current.CancellationToken);

        Assert.Equal("Monitor", result.Name);
        await _auditMock.Received(1).LogAsync("Registered", "Plugin", Arg.Any<int?>(), "Monitor v1.0", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UpdatePluginAsync_NotFound_ReturnsNull()
    {
        _repoMock.FindAsync(99, TestContext.Current.CancellationToken).Returns((PluginRegistration?)null);

        Assert.Null(await _sut.UpdatePluginAsync(99, new UpdatePluginRequest { Status = PluginStatus.Enabled }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdatePluginAsync_Found_UpdatesAndReturns()
    {
        _repoMock.FindAsync(1, TestContext.Current.CancellationToken)
            .Returns(new PluginRegistration { Id = 1, Name = "X", Version = "1.0", Status = PluginStatus.Registered });
        _repoMock.SaveChangesAsync(TestContext.Current.CancellationToken).Returns(Task.CompletedTask);

        var result = await _sut.UpdatePluginAsync(1, new UpdatePluginRequest { Status = PluginStatus.Enabled, Description = "Updated" }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(PluginStatus.Enabled, result.Status);
        Assert.Equal("Updated", result.Description);
    }

    [Fact]
    public async Task UnregisterPluginAsync_NotFound_ReturnsFalse()
    {
        _repoMock.FindAsync(99, TestContext.Current.CancellationToken).Returns((PluginRegistration?)null);

        Assert.False(await _sut.UnregisterPluginAsync(99, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnregisterPluginAsync_Found_RemovesAndReturnsTrue()
    {
        var entity = new PluginRegistration { Id = 1, Name = "Old" };
        _repoMock.FindAsync(1, TestContext.Current.CancellationToken).Returns(entity);
        _repoMock.RemoveAsync(entity, TestContext.Current.CancellationToken).Returns(Task.CompletedTask);

        Assert.True(await _sut.UnregisterPluginAsync(1, ct: TestContext.Current.CancellationToken));
        await _repoMock.Received(1).RemoveAsync(entity, TestContext.Current.CancellationToken);
    }
}
