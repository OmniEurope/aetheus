// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ReleasesSection = Aetheus.Front.Pages.Projects.Sections.Releases;

namespace Aetheus.Front.Tests.Pages.Projects;

/// <summary>
/// Tests for the thin routing-layer section pages under Pages/Projects/Sections/.
/// Focus: Releases section (14 missed lines at 0% coverage).
/// </summary>
public class ProjectSectionsTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ProjectSectionsTests() => _handler = BunitTestHelper.RegisterServices(this);

    private ProjectDetailLoader CreateLoader() => new(
        Services.GetRequiredService<ApiClient>(),
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>(),
        NullLogger<ProjectDetailLoader>.Instance,
        Services.GetRequiredService<HubConnectionFactory>());

    // === Releases section ===

    [Fact]
    public async Task Releases_WithLoader_EnsuresProjectForBoundId()
    {
        // The Id parameter must actually flow into Loader.EnsureLoadedAsync(Id): we prove it
        // by checking the loaded project carries that exact id (not just that the property
        // round-trips a value).
        _handler.SetJsonResponse("api/projects/10", new ProjectDetailDto { Id = 10, Name = "P10" });
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto> { Items = [], TotalCount = 0 });

        var loader = CreateLoader();
        var component = new ReleasesSection();
        typeof(ReleasesSection).GetProperty("Id")!.SetValue(component, 10);
        typeof(ReleasesSection).GetProperty("Loader")!.SetValue(component, loader);

        await InvokeOnParametersSetAsync(component);

        Assert.NotNull(loader.Project);
        Assert.Equal(10, loader.Project!.Id);
    }

    [Fact]
    public async Task Releases_NullLoader_DoesNotThrow_AndLoadsNothing()
    {
        var component = new ReleasesSection();
        typeof(ReleasesSection).GetProperty("Id")!.SetValue(component, 99);
        typeof(ReleasesSection).GetProperty("Loader")!.SetValue(component, null);

        // OnParametersSetAsync must guard the null cascade - no loader, no exception, no call.
        var ex = await Record.ExceptionAsync(() => InvokeOnParametersSetAsync(component));
        Assert.Null(ex);
        Assert.Null(component.Loader);
    }

    [Fact]
    public async Task Releases_OnParametersSetAsync_WithLoader_CallsEnsureLoaded()
    {
        _handler.SetJsonResponse("api/projects/3", new ProjectDetailDto { Id = 3, Name = "P3" });
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto> { Items = [], TotalCount = 0 });

        var loader = CreateLoader();

        var component = new ReleasesSection();
        typeof(ReleasesSection).GetProperty("Id")!.SetValue(component, 3);
        typeof(ReleasesSection).GetProperty("Loader")!.SetValue(component, loader);

        await InvokeOnParametersSetAsync(component);

        Assert.NotNull(loader.Project);
        Assert.Equal(3, loader.Project!.Id);
    }

    private static Task InvokeOnParametersSetAsync(ReleasesSection component)
    {
        var method = typeof(ReleasesSection).GetMethod("OnParametersSetAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(component, [])!;
    }
}
