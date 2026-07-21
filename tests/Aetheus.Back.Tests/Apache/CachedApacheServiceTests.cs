// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Apache;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class CachedApacheServiceTests
{
    private readonly IApacheService _innerMock = Substitute.For<IApacheService>();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly CachedApacheService _sut;

    public CachedApacheServiceTests()
    {
        _sut = new CachedApacheService(_innerMock, _cache);
    }

    [Fact]
    public async Task GetStateAsync_CachesResult()
    {
        _innerMock.GetStateAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ApacheDataDto { IsRunning = true });

        var result1 = await _sut.GetStateAsync(1, ct: TestContext.Current.CancellationToken);
        var result2 = await _sut.GetStateAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.True(result1.IsRunning);
        Assert.True(result2.IsRunning);
        await _innerMock.Received(1).GetStateAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetModulesAsync_CachesResult()
    {
        _innerMock.GetModulesAsync(1, Arg.Any<CancellationToken>())
            .Returns([new ApacheModuleDto { Name = "mod_ssl", IsEnabled = true }]);

        var result1 = await _sut.GetModulesAsync(1, ct: TestContext.Current.CancellationToken);
        var result2 = await _sut.GetModulesAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Single(result1);
        await _innerMock.Received(1).GetModulesAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetVirtualHostsAsync_CachesResult()
    {
        _innerMock.GetVirtualHostsAsync(1, Arg.Any<CancellationToken>())
            .Returns([]);

        await _sut.GetVirtualHostsAsync(1, ct: TestContext.Current.CancellationToken);
        await _sut.GetVirtualHostsAsync(1, ct: TestContext.Current.CancellationToken);

        await _innerMock.Received(1).GetVirtualHostsAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteActionAsync_InvalidatesCache()
    {
        _innerMock.GetStateAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ApacheDataDto());

        await _sut.GetStateAsync(1, ct: TestContext.Current.CancellationToken);
        await _sut.ExecuteActionAsync(1, new ApacheActionRequest { Action = ApacheAction.Restart }, ct: TestContext.Current.CancellationToken);
        await _sut.GetStateAsync(1, ct: TestContext.Current.CancellationToken);

        await _innerMock.Received(2).GetStateAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveVHostConfigAsync_InvalidatesCache()
    {
        _innerMock.GetVirtualHostsAsync(1, Arg.Any<CancellationToken>())
            .Returns([]);

        await _sut.GetVirtualHostsAsync(1, ct: TestContext.Current.CancellationToken);
        await _sut.SaveVHostConfigAsync(1, new ApacheVHostSaveRequest { SiteName = "s", Content = "c" }, ct: TestContext.Current.CancellationToken);
        await _sut.GetVirtualHostsAsync(1, ct: TestContext.Current.CancellationToken);

        await _innerMock.Received(2).GetVirtualHostsAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLogsAsync_DelegatesWithoutCaching()
    {
        await _sut.GetLogsAsync(1, new ApacheLogRequest { LogType = "access", Lines = 100 }, ct: TestContext.Current.CancellationToken);

        await _innerMock.Received(1).GetLogsAsync(1, Arg.Any<ApacheLogRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetVHostConfigAsync_DelegatesWithoutCaching()
    {
        await _sut.GetVHostConfigAsync(1, "site.com", ct: TestContext.Current.CancellationToken);

        await _innerMock.Received(1).GetVHostConfigAsync(1, "site.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetHtaccessAsync_DelegatesWithoutCaching()
    {
        await _sut.GetHtaccessAsync(1, "/var/www", ct: TestContext.Current.CancellationToken);

        await _innerMock.Received(1).GetHtaccessAsync(1, "/var/www", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveHtaccessAsync_Delegates()
    {
        await _sut.SaveHtaccessAsync(1, new ApacheHtaccessSaveRequest { DocumentRoot = "/var/www", Content = "c" }, ct: TestContext.Current.CancellationToken);

        await _innerMock.Received(1).SaveHtaccessAsync(1, Arg.Any<ApacheHtaccessSaveRequest>(), Arg.Any<CancellationToken>());
    }
}
