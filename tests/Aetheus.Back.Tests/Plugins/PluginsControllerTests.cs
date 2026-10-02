// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Plugins;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class PluginsControllerTests
{
    private readonly IPluginService _serviceMock = Substitute.For<IPluginService>();
    private readonly PluginsController _sut;

    public PluginsControllerTests()
    {
        _sut = new PluginsController(_serviceMock);
    }

    [Fact]
    public async Task GetPlugins_ReturnsOk()
    {
        _serviceMock.GetPluginsPageAsync(Arg.Any<PaginationRequest>(), TestContext.Current.CancellationToken)
            .Returns(new PaginatedResult<PluginRegistrationDto>
            {
                Items = [new PluginRegistrationDto { Id = 1, Name = "GitSync" }],
                TotalCount = 1
            });

        var result = await _sut.GetPlugins(new PaginationRequest(), TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(((PaginatedResult<PluginRegistrationDto>)ok.Value!).Items);
    }

    [Fact]
    public async Task GetPlugin_NotFound_Returns404()
    {
        _serviceMock.GetPluginAsync(99, TestContext.Current.CancellationToken).Returns((PluginRegistrationDto?)null);

        var result = await _sut.GetPlugin(99, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPlugin_Found_ReturnsOk()
    {
        _serviceMock.GetPluginAsync(1, TestContext.Current.CancellationToken)
            .Returns(new PluginRegistrationDto { Id = 1, Name = "Monitor" });

        var result = await _sut.GetPlugin(1, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Monitor", ((PluginRegistrationDto)ok.Value!).Name);
    }

    [Fact]
    public async Task RegisterPlugin_ReturnsCreated()
    {
        var request = new RegisterPluginRequest { Name = "NewPlugin", Version = "1.0" };
        _serviceMock.RegisterPluginAsync(request, TestContext.Current.CancellationToken)
            .Returns(new PluginRegistrationDto { Id = 10, Name = "NewPlugin" });

        var result = await _sut.RegisterPlugin(request, TestContext.Current.CancellationToken);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(10, ((PluginRegistrationDto)created.Value!).Id);
    }

    [Fact]
    public async Task UpdatePlugin_NotFound_Returns404()
    {
        _serviceMock.UpdatePluginAsync(99, Arg.Any<UpdatePluginRequest>(), TestContext.Current.CancellationToken)
            .Returns((PluginRegistrationDto?)null);

        var result = await _sut.UpdatePlugin(99, new UpdatePluginRequest { Status = PluginStatus.Enabled }, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task UnregisterPlugin_NotFound_Returns404()
    {
        _serviceMock.UnregisterPluginAsync(99, TestContext.Current.CancellationToken).Returns(false);

        var result = await _sut.UnregisterPlugin(99, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task UnregisterPlugin_Deleted_ReturnsNoContent()
    {
        _serviceMock.UnregisterPluginAsync(1, TestContext.Current.CancellationToken).Returns(true);

        var result = await _sut.UnregisterPlugin(1, TestContext.Current.CancellationToken);

        Assert.IsType<NoContentResult>(result);
    }
}
