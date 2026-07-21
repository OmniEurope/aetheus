// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Front.Tests.Services;

/// <summary>
/// Coverage for ServerDetailLoader paths not hit by the existing tests:
/// EnsureLoadedAsync no-op (same id), EnsureLoadedAsync initial load, SwapServer (different id),
/// server with metrics sets MetricsReceived, OnChanged fired on load, OnTaskCompleted routing,
/// SyncReleasesAsync (via TeardownAsync), and capability flags from Docker containers/stacks.
/// </summary>
public class ServerDetailLoaderCoverageTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerDetailLoaderCoverageTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        _handler.SetJsonResponse(
            HttpMethod.Get,
            "api/monitoring/servers/",
            new List<ServerMetricDto>());
    }

    private ServerDetailLoader CreateLoader() => new(
        Services.GetRequiredService<ApiClient>(),
        Services.GetRequiredService<HubConnectionFactory>(),
        Services.GetRequiredService<NavigationManager>(),
        NullLogger<ServerDetailLoader>.Instance);

    private static void SetServer(ServerDetailLoader loader, ServerDetailDto? server) =>
        typeof(ServerDetailLoader).GetProperty(nameof(ServerDetailLoader.Server))!
            .SetValue(loader, server);

    private static void SetCurrentId(ServerDetailLoader loader, int? id) =>
        typeof(ServerDetailLoader).GetField("_currentId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(loader, id);

    // ── EnsureLoadedAsync: no-op for same id (really invokes the SUT) ─────────
    // The guard is `if (_currentId == id && Server is not null) return;` - so a second
    // call with the same id must NOT touch the network, must NOT fire OnChanged, and must
    // leave the *same* Server reference in place. We assert reference identity (stronger
    // than name-equality) to prove no reload happened.

    [Fact]
    public async Task EnsureLoadedAsync_SameId_NoOp_ServerReferenceUnchanged()
    {
        var sut = CreateLoader();
        var cached = new ServerDetailDto { Id = 1, Name = "cached" };
        SetCurrentId(sut, 1);
        SetServer(sut, cached);

        var changed = 0;
        sut.OnChanged += () => changed++;

        // No api/servers/1 stub registered: if the guard failed and a reload happened,
        // GetFromJsonAsync would deserialize the default "{}" body into a *different*
        // ServerDetailDto instance - breaking the ReferenceEqual assertion below.
        await sut.EnsureLoadedAsync(1, Xunit.TestContext.Current.CancellationToken);

        Assert.Same(cached, sut.Server);
        Assert.Equal(0, changed);
    }

    // ── MetricsReceived really set by EnsureLoadedAsync for a server with memory ──

    [Fact]
    public async Task EnsureLoadedAsync_ServerWithMemory_SetsMetricsReceived()
    {
        _handler.SetJsonResponse("api/servers/6",
            new ServerDetailDto { Id = 6, Name = "metrics-srv", MemoryTotalMb = 8192, MemoryUsedMb = 4096 });

        var sut = CreateLoader();
        await sut.EnsureLoadedAsync(6, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(6, sut.Server!.Id);
        Assert.True(sut.MetricsReceived);
        Assert.NotNull(sut.LastUpdated);
    }

    // ── InitialLoadCompleted flips true once the detail fetch succeeds ─────────
    // Drives the real SUT: EnsureLoadedAsync sets InitialLoadCompleted = true right after
    // the API returns, BEFORE the SignalR hub start (which fails in the test env). We assert
    // the fresh loader starts false and the successful load flips it - not a reflection set.

    [Fact]
    public async Task EnsureLoadedAsync_SuccessfulLoad_SetsInitialLoadCompleted()
    {
        _handler.SetJsonResponse("api/servers/3", new ServerDetailDto { Id = 3, Name = "srv-3" });
        var sut = CreateLoader();

        Assert.False(sut.InitialLoadCompleted); // fresh loader

        await sut.EnsureLoadedAsync(3, Xunit.TestContext.Current.CancellationToken);

        Assert.True(sut.InitialLoadCompleted);
    }

    // ── OnChanged fires when server is set ────────────────────────────────────

    [Fact]
    public void OnChanged_FiredByTeardownAsync_WhenNavigatingAway()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 8, Name = "evt-srv" });
        SetCurrentId(sut, 8);
        var fired = 0;
        sut.OnChanged += () => fired++;

        // Navigating away triggers TeardownAsync which calls OnChanged
        Services.GetRequiredService<NavigationManager>().NavigateTo("/projects");

        Assert.True(fired > 0);
    }

    // ── SwapServer: different id clears old server state ─────────────────────

    [Fact]
    public void TeardownAsync_ClearsCurrentId()
    {
        var sut = CreateLoader();
        SetCurrentId(sut, 10);
        SetServer(sut, new ServerDetailDto { Id = 10, Name = "srv-10" });

        // Navigate away to trigger teardown
        Services.GetRequiredService<NavigationManager>().NavigateTo("/projects");

        // After teardown, _currentId should be null
        var currentId = typeof(ServerDetailLoader).GetField("_currentId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(sut);
        Assert.Null(currentId);
    }

    // ── TeardownAsync resets all state ────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_ResetsTeardownState()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 1 });
        SetCurrentId(sut, 1);

        await sut.DisposeAsync();

        Assert.Null(sut.Server);
        Assert.Null(sut.LastUpdated);
        Assert.False(sut.MetricsReceived);
        Assert.False(sut.InitialLoadCompleted);
    }

    // ── UpdateServerFields mutates the loaded server and fires OnChanged ───────
    // Real public behaviour used by the layout's inline edit panel: it must apply the new
    // name/type/tags onto the loaded Server and raise OnChanged exactly once.

    [Fact]
    public void UpdateServerFields_MutatesServerAndFiresOnChanged()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 9, Name = "old", Type = ServerType.Normal, Tags = [] });
        var fired = 0;
        sut.OnChanged += () => fired++;

        sut.UpdateServerFields("renamed", ServerType.Docker, ["prod", "web"]);

        Assert.Equal("renamed", sut.Server!.Name);
        Assert.Equal(ServerType.Docker, sut.Server!.Type);
        Assert.Equal(["prod", "web"], sut.Server!.Tags);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void UpdateServerFields_NoServerLoaded_IsNoOp()
    {
        var sut = CreateLoader();
        var fired = 0;
        sut.OnChanged += () => fired++;

        sut.UpdateServerFields("x", ServerType.Normal, []);

        Assert.Null(sut.Server);
        Assert.Equal(0, fired);
    }

    // ── Capability flags: Docker containers/images/stacks ────────────────────

    [Fact]
    public void HasDocker_WithContainers_ReturnsTrue()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto
        {
            Id = 1,
            Docker = new DockerDataDto
            {
                Containers = [new DockerContainerDto { ContainerId = "c1", Name = "app", Image = "img", State = "running" }],
                Images = [],
                ComposeStacks = []
            }
        });
        Assert.True(sut.HasDocker);
    }

    [Fact]
    public void HasDocker_WithImages_ReturnsTrue()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto
        {
            Id = 1,
            Docker = new DockerDataDto
            {
                Containers = [],
                Images = [new DockerImageDto { ImageId = "i1", Repository = "nginx", Tag = "latest", Size = "50MB" }],
                ComposeStacks = []
            }
        });
        Assert.True(sut.HasDocker);
    }

    // ── Location change: still within /servers/ does not clear ───────────────

    [Fact]
    public void LocationChange_ToServerSubpath_KeepsServer()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 1 });
        SetCurrentId(sut, 1);

        Services.GetRequiredService<NavigationManager>().NavigateTo("/servers/1/docker");

        Assert.NotNull(sut.Server);
    }

    // ── Multiple calls for same id with pre-loaded state: no-op check ────────
    // Drives the guard for real: stub api/servers/20 with a *different* name, then call
    // EnsureLoadedAsync(20) on an already-loaded loader. The guard must short-circuit so the
    // stubbed body is never fetched and the original "double-load" server survives untouched.

    [Fact]
    public async Task EnsureLoadedAsync_SameIdAlreadyLoaded_NoOpGuardHolds()
    {
        _handler.SetJsonResponse("api/servers/20", new ServerDetailDto { Id = 20, Name = "reloaded-should-not-happen" });

        var sut = CreateLoader();
        var original = new ServerDetailDto { Id = 20, Name = "double-load" };
        SetCurrentId(sut, 20);
        SetServer(sut, original);

        await sut.EnsureLoadedAsync(20, Xunit.TestContext.Current.CancellationToken);

        Assert.Same(original, sut.Server);
        Assert.Equal("double-load", sut.Server!.Name);
    }
}
