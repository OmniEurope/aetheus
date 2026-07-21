// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Dashboards;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class DashboardServiceTests
{
    private readonly IDashboardRepository _repoMock = Substitute.For<IDashboardRepository>();
    private readonly IAuditService _auditMock = Substitute.For<IAuditService>();
    private readonly DashboardService _sut;

    public DashboardServiceTests()
    {
        _auditMock.LogAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _sut = new DashboardService(_repoMock, _auditMock, TimeProvider.System);
    }

    [Fact]
    public async Task GetUserDashboardsAsync_ReturnsMappedDtos()
    {
        _repoMock.GetByUserIdAsync(1, TestContext.Current.CancellationToken)
            .Returns([new Dashboard { Id = 1, UserId = 1, Name = "Main", Widgets = [] }]);

        var result = await _sut.GetUserDashboardsAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("Main", result[0].Name);
    }

    [Fact]
    public async Task GetDashboardAsync_NotFound_ReturnsNull()
    {
        _repoMock.GetDetailAsync(99, TestContext.Current.CancellationToken).Returns((Dashboard?)null);

        var result = await _sut.GetDashboardAsync(99, 1, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDashboardAsync_Found_ReturnsDtoWithWidgets()
    {
        var dashboard = new Dashboard
        {
            Id = 1,
            UserId = 1,
            Name = "Ops",
            Widgets = [new DashboardWidget { Id = 10, WidgetType = DashboardWidgetType.ServerCount, Title = "Servers" }]
        };
        _repoMock.GetDetailAsync(1, TestContext.Current.CancellationToken).Returns(dashboard);

        var result = await _sut.GetDashboardAsync(1, 1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Widgets);
        Assert.Equal("Servers", result.Widgets[0].Title);
    }

    [Fact]
    public async Task CreateDashboardAsync_IsDefault_ClearsExistingDefaults()
    {
        var request = new CreateDashboardRequest { Name = "New", IsDefault = true, Widgets = [] };
        _repoMock.AddAsync(Arg.Any<Dashboard>(), TestContext.Current.CancellationToken).Returns(Task.CompletedTask);

        await _sut.CreateDashboardAsync(1, request, ct: TestContext.Current.CancellationToken);

        await _repoMock.Received(1).ClearDefaultsAsync(1, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CreateDashboardAsync_NotDefault_DontClearDefaults()
    {
        var request = new CreateDashboardRequest { Name = "New", IsDefault = false, Widgets = [] };
        _repoMock.AddAsync(Arg.Any<Dashboard>(), TestContext.Current.CancellationToken).Returns(Task.CompletedTask);

        await _sut.CreateDashboardAsync(1, request, ct: TestContext.Current.CancellationToken);

        await _repoMock.DidNotReceive().ClearDefaultsAsync(Arg.Any<int>(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CreateDashboardAsync_MapsWidgets()
    {
        Dashboard? captured = null;
        _repoMock.AddAsync(Arg.Any<Dashboard>(), TestContext.Current.CancellationToken)
            .Returns(Task.CompletedTask)
            .AndDoes(ci => { captured = ci.Arg<Dashboard>(); });

        var request = new CreateDashboardRequest
        {
            Name = "Test",
            Widgets = [new CreateDashboardWidgetRequest { WidgetType = DashboardWidgetType.PipelineActivity, Title = "Pipes", Width = 2, Height = 1 }]
        };

        await _sut.CreateDashboardAsync(1, request, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Single(captured.Widgets);
        Assert.Equal(DashboardWidgetType.PipelineActivity, captured.Widgets[0].WidgetType);
    }

    [Fact]
    public async Task UpdateDashboardAsync_NotFound_ReturnsNull()
    {
        _repoMock.FindAsync(99, TestContext.Current.CancellationToken).Returns((Dashboard?)null);

        var result = await _sut.UpdateDashboardAsync(99, 1, new UpdateDashboardRequest { Name = "X", Widgets = [] }, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateDashboardAsync_WrongUser_ReturnsNull()
    {
        _repoMock.FindAsync(1, TestContext.Current.CancellationToken)
            .Returns(new Dashboard { Id = 1, UserId = 2, Widgets = [] });

        var result = await _sut.UpdateDashboardAsync(1, 1, new UpdateDashboardRequest { Name = "X", Widgets = [] }, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateDashboardAsync_Valid_UpdatesAndReturns()
    {
        var rowVersion = Guid.NewGuid();
        _repoMock.FindAsync(1, TestContext.Current.CancellationToken)
            .Returns(new Dashboard { Id = 1, UserId = 1, Name = "Old", RowVersion = rowVersion, Widgets = [] });
        _repoMock.SaveChangesAsync(TestContext.Current.CancellationToken).Returns(Task.CompletedTask);

        var result = await _sut.UpdateDashboardAsync(1, 1, new UpdateDashboardRequest { Name = "New", RowVersion = rowVersion, Widgets = [] }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("New", result.Name);
    }

    [Fact]
    public async Task UpdateDashboardAsync_SetDefault_ClearsOtherDefaults()
    {
        var rowVersion = Guid.NewGuid();
        _repoMock.FindAsync(1, TestContext.Current.CancellationToken)
            .Returns(new Dashboard { Id = 1, UserId = 1, Name = "D", IsDefault = false, RowVersion = rowVersion, Widgets = [] });
        _repoMock.ClearDefaultsAsync(1, TestContext.Current.CancellationToken).Returns(Task.CompletedTask);
        _repoMock.SaveChangesAsync(TestContext.Current.CancellationToken).Returns(Task.CompletedTask);

        var result = await _sut.UpdateDashboardAsync(1, 1, new UpdateDashboardRequest
        {
            Name = "D",
            IsDefault = true,
            RowVersion = rowVersion,
            Widgets = [new CreateDashboardWidgetRequest { WidgetType = DashboardWidgetType.ServerCount, Title = "W", Width = 1, Height = 1 }]
        }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        await _repoMock.Received(1).ClearDefaultsAsync(1, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UpdateDashboardAsync_StaleRowVersion_ThrowsConflict()
    {
        _repoMock.FindAsync(1, TestContext.Current.CancellationToken)
            .Returns(new Dashboard { Id = 1, UserId = 1, Name = "Old", RowVersion = Guid.NewGuid(), Widgets = [] });

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.UpdateDashboardAsync(1, 1, new UpdateDashboardRequest { Name = "New", RowVersion = Guid.NewGuid(), Widgets = [] }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteDashboardAsync_NotFound_ReturnsFalse()
    {
        _repoMock.FindAsync(99, TestContext.Current.CancellationToken).Returns((Dashboard?)null);

        Assert.False(await _sut.DeleteDashboardAsync(99, 1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteDashboardAsync_WrongUser_ReturnsFalse()
    {
        _repoMock.FindAsync(1, TestContext.Current.CancellationToken)
            .Returns(new Dashboard { Id = 1, UserId = 2, Widgets = [] });

        Assert.False(await _sut.DeleteDashboardAsync(1, 1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteDashboardAsync_Valid_ReturnsTrue()
    {
        var entity = new Dashboard { Id = 1, UserId = 1, Widgets = [] };
        _repoMock.FindAsync(1, TestContext.Current.CancellationToken).Returns(entity);
        _repoMock.RemoveAsync(entity, TestContext.Current.CancellationToken).Returns(Task.CompletedTask);

        Assert.True(await _sut.DeleteDashboardAsync(1, 1, ct: TestContext.Current.CancellationToken));
        await _repoMock.Received(1).RemoveAsync(entity, TestContext.Current.CancellationToken);
    }
}
