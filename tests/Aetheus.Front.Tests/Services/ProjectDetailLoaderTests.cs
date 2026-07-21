// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Front.Tests.Services;

public class ProjectDetailLoaderTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ProjectDetailLoaderTests() => _handler = BunitTestHelper.RegisterServices(this);

    private ProjectDetailLoader CreateLoader() => new(
        Services.GetRequiredService<ApiClient>(),
        Services.GetRequiredService<NavigationManager>(),
        NullLogger<ProjectDetailLoader>.Instance,
        Services.GetRequiredService<HubConnectionFactory>());

    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    private void StubProject(int id, params ReleaseDto[] releases)
    {
        _handler.SetJsonResponse($"api/projects/{id}", new ProjectDetailDto { Id = id, Name = $"P{id}" });
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto>
        {
            Items = [.. releases],
            TotalCount = releases.Length
        });
    }

    [Fact]
    public void InitialState_IsEmpty()
    {
        var sut = CreateLoader();
        Assert.Null(sut.Project);
        Assert.Empty(sut.Releases);
        Assert.False(sut.InitialLoadCompleted);
    }

    [Fact]
    public async Task EnsureLoadedAsync_LoadsProjectAndReleases_FiresOnChanged()
    {
        StubProject(1, new ReleaseDto { Id = 5, Version = "1.0" });
        var sut = CreateLoader();
        var changed = 0;
        sut.OnChanged += () => changed++;

        await sut.EnsureLoadedAsync(1, Xunit.TestContext.Current.CancellationToken);

        Assert.NotNull(sut.Project);
        Assert.Equal(1, sut.Project!.Id);
        Assert.Single(sut.Releases);
        Assert.Equal(5, sut.Releases[0].Id);
        Assert.True(sut.InitialLoadCompleted);
        Assert.True(changed > 0);
    }

    [Fact]
    public async Task EnsureLoadedAsync_SameId_NoOps()
    {
        StubProject(1);
        var sut = CreateLoader();
        await sut.EnsureLoadedAsync(1, Xunit.TestContext.Current.CancellationToken);

        var changed = 0;
        sut.OnChanged += () => changed++;
        await sut.EnsureLoadedAsync(1, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task EnsureLoadedAsync_DifferentId_SwapsState()
    {
        StubProject(1);
        _handler.SetJsonResponse("api/projects/2", new ProjectDetailDto { Id = 2, Name = "P2" });
        var sut = CreateLoader();
        await sut.EnsureLoadedAsync(1, Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(1, sut.Project!.Id);

        await sut.EnsureLoadedAsync(2, Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(2, sut.Project!.Id);
    }

    [Fact]
    public async Task ForceReloadAsync_RefetchesForSameId()
    {
        StubProject(1);
        var sut = CreateLoader();
        await sut.EnsureLoadedAsync(1, Xunit.TestContext.Current.CancellationToken);

        var changed = 0;
        sut.OnChanged += () => changed++;
        await sut.ForceReloadAsync(1, Xunit.TestContext.Current.CancellationToken);

        Assert.NotNull(sut.Project);
        Assert.True(changed > 0);
    }

    [Fact]
    public async Task SyncReleasesAsync_UpdatesReleases_FiresOnChanged()
    {
        _handler.SetJsonResponse("api/releases/sync/1", new List<ReleaseDto>());
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto>
        {
            Items = [new ReleaseDto { Id = 9, Version = "2.0" }],
            TotalCount = 1
        });
        var sut = CreateLoader();
        var changed = 0;
        sut.OnChanged += () => changed++;

        await sut.SyncReleasesAsync(1, Xunit.TestContext.Current.CancellationToken);

        Assert.Single(sut.Releases);
        Assert.Equal(9, sut.Releases[0].Id);
        Assert.True(changed > 0);
    }

    [Fact]
    public async Task NavigatingAwayFromProjects_TearsDown()
    {
        StubProject(1);
        var sut = CreateLoader();
        await sut.EnsureLoadedAsync(1, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(sut.Project);

        Nav.NavigateTo("/servers");

        Assert.Null(sut.Project);
        Assert.False(sut.InitialLoadCompleted);
    }

    [Fact]
    public async Task NavigatingWithinProjects_KeepsState()
    {
        StubProject(1);
        var sut = CreateLoader();
        await sut.EnsureLoadedAsync(1, Xunit.TestContext.Current.CancellationToken);

        Nav.NavigateTo("/projects/1/pipelines");

        Assert.NotNull(sut.Project);
    }

    [Fact]
    public async Task DisposeAsync_TearsDownAndUnsubscribes()
    {
        StubProject(1);
        var sut = CreateLoader();
        await sut.EnsureLoadedAsync(1, Xunit.TestContext.Current.CancellationToken);

        await sut.DisposeAsync();
        Assert.Null(sut.Project);

        // Already unsubscribed - navigation must not resurrect teardown or throw.
        Nav.NavigateTo("/servers");
    }
}
