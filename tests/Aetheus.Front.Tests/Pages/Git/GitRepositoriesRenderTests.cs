// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Git;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Git;

public class GitRepositoriesRenderTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type PageType = typeof(GitRepositories);

    public GitRepositoriesRenderTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items = [new ProjectDto { Id = 1, Name = "MyProject", Status = ProjectStatus.Active }],
            TotalCount = 1
        });
        _handler.SetPaginatedJsonResponse("api/git/repos", new List<GitLightRepoDto>());
    }

    [Fact]
    public void Renders_ToolbarAndFilter_WithoutError()
    {
        var cut = Render<GitRepositories>();
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"), TimeSpan.FromSeconds(2));

        // The page renders its toolbar (New Repository action) and the project filter dropdown,
        // and OnInitializedAsync loads the project list that populates the filter.
        Assert.Contains("NewRepository", cut.Markup);
        Assert.Contains("SelectProject", cut.Markup);
        Assert.Contains(_handler.Requests, r => r.Method == "GET" && r.Url.Contains("api/projects"));
    }

    [Fact]
    public async Task OnInitializedAsync_LoadsProjects()
    {
        var cut = Render<GitRepositories>();
        await cut.InvokeAsync(() => Task.CompletedTask);
        var projects = (List<ProjectDto>)PageType.GetField("_projects", Priv)!.GetValue(cut.Instance)!;
        Assert.NotEmpty(projects);
    }

    [Fact]
    public async Task LoadData_WithNoProjectFilter_ClearsRepos()
    {
        var cut = Render<GitRepositories>();
        await cut.InvokeAsync(() => Task.CompletedTask);
        var method = PageType.GetMethod("LoadData", Priv)!;
        PageType.GetField("_projectFilter", Priv)!.SetValue(cut.Instance, null);
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);
        var repos = (List<GitLightRepoDto>)PageType.GetField("_repos", Priv)!.GetValue(cut.Instance)!;
        Assert.Empty(repos);
    }

    [Fact]
    public async Task LoadData_WithProjectFilter_CallsApi()
    {
        _handler.SetPaginatedJsonResponse("api/git/repos", new List<GitLightRepoDto>
        {
            new() { Id = 1, Name = "my-repo", Slug = "my-repo", ProjectId = 1 }
        });
        var cut = Render<GitRepositories>();
        await cut.InvokeAsync(() => Task.CompletedTask);
        var method = PageType.GetMethod("LoadData", Priv)!;
        PageType.GetField("_projectFilter", Priv)!.SetValue(cut.Instance, 1);
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);
        var repos = (List<GitLightRepoDto>)PageType.GetField("_repos", Priv)!.GetValue(cut.Instance)!;
        Assert.Single(repos);
        Assert.Equal("my-repo", repos[0].Name);
    }

    [Fact]
    public async Task ClearFilters_ResetsProjectFilter()
    {
        var cut = Render<GitRepositories>();
        await cut.InvokeAsync(() => Task.CompletedTask);
        PageType.GetField("_projectFilter", Priv)!.SetValue(cut.Instance, 1);
        var method = PageType.GetMethod("ClearFilters", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);
        var filter = (int?)PageType.GetField("_projectFilter", Priv)!.GetValue(cut.Instance);
        Assert.Null(filter);
    }

    [Fact]
    public async Task OnProjectFilterChanged_ReloadsData()
    {
        var cut = Render<GitRepositories>();
        await cut.InvokeAsync(() => Task.CompletedTask);
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("git-repositories?projectId=1");
        PageType.GetField("_projectFilter", Priv)!.SetValue(cut.Instance, 1);
        _handler.Requests.Clear();
        var method = PageType.GetMethod("OnProjectFilterChanged", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [new object()])!);
        // With a project filter set, the change handler reloads via GET api/git/repos?projectId=1.
        var requestUrl = _handler.Requests.Last(r => r.Method == "GET" && r.Url.Contains("api/git/repos")).Url;
        Assert.Contains("projectId=1", requestUrl);
    }

    [Fact]
    public void ApplyProjectFilterFromQuery_WithNullProjectId_ClearsFilter()
    {
        var cut = Render<GitRepositories>();
        // ProjectId parameter not set => null
        var method = PageType.GetMethod("ApplyProjectFilterFromQuery", Priv)!;
        method.Invoke(cut.Instance, []);
        var filter = (int?)PageType.GetField("_projectFilter", Priv)!.GetValue(cut.Instance);
        Assert.Null(filter);
    }

    [Fact]
    public void ApplyProjectFilterFromQuery_WithUnknownProjectId_ClearsFilter()
    {
        var cut = Render<GitRepositories>();
        // Set a ProjectId that isn't in the loaded projects list
        var prop = PageType.GetProperty("ProjectId")!;
        prop.SetValue(cut.Instance, 999);
        var method = PageType.GetMethod("ApplyProjectFilterFromQuery", Priv)!;
        method.Invoke(cut.Instance, []);
        var filter = (int?)PageType.GetField("_projectFilter", Priv)!.GetValue(cut.Instance);
        Assert.Null(filter);
    }

    [Fact]
    public void RefreshCanWrite_MirrorsPermissionServiceState()
    {
        var cut = Render<GitRepositories>();
        var permissions = Services.GetRequiredService<PermissionService>();
        var method = PageType.GetMethod("RefreshCanWrite", Priv)!;

        // With Write on Project, RefreshCanWrite yields true.
        method.Invoke(cut.Instance, []);
        Assert.True((bool)PageType.GetField("_canWrite", Priv)!.GetValue(cut.Instance)!);

        // Revoke all permissions: RefreshCanWrite must now yield false (it reads the live service).
        permissions.SetPermissions([], isAdmin: false);
        method.Invoke(cut.Instance, []);
        Assert.False((bool)PageType.GetField("_canWrite", Priv)!.GetValue(cut.Instance)!);
    }

    [Fact]
    public async Task OnPermissionsChanged_UpdatesCanWriteFromEvent()
    {
        var cut = Render<GitRepositories>();
        var permissions = Services.GetRequiredService<PermissionService>();

        // The subscribed handler starts true (test user holds Write on Project)...
        Assert.True((bool)PageType.GetField("_canWrite", Priv)!.GetValue(cut.Instance)!);

        // ...and reacts to a real permission event: revoking Write drives _canWrite to false.
        await cut.InvokeAsync(() => permissions.SetPermissions([], isAdmin: false));
        Assert.False((bool)PageType.GetField("_canWrite", Priv)!.GetValue(cut.Instance)!);
    }

    [Fact]
    public async Task DisposeAsync_UnsubscribesFromPermissionChanges()
    {
        var cut = Render<GitRepositories>();
        var permissions = Services.GetRequiredService<PermissionService>();

        await cut.InvokeAsync(() => cut.Instance.DisposeAsync().AsTask());

        // Pin _canWrite false, then raise a permission event granting Write. A still-subscribed
        // handler would flip it true; after dispose it must stay false (handler detached).
        PageType.GetField("_canWrite", Priv)!.SetValue(cut.Instance, false);
        var writeAll = Enum.GetValues<ResourceType>()
            .Select(rt => new EffectivePermissionDto { ResourceType = rt, ResourceId = null, Permission = Permission.Write })
            .ToList();
        permissions.SetPermissions(writeAll, isAdmin: false);
        Assert.False((bool)PageType.GetField("_canWrite", Priv)!.GetValue(cut.Instance)!);
    }
}
