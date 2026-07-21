// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Services;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Shared;

// 0-a: the shared ReleasesList drives all 3 scopes. Server scope is exercised by
// ServerReleasesSectionTests; here we cover the global (unfiltered) and project-scoped paths plus
// the behavioral methods (load / sync / filter / permissions / dispose) that the global Releases
// page used to own - consolidating the previously duplicated per-page coverage onto the component
// where the logic now lives. Badge mapping is covered by ReleaseHelperTests.
public class ReleasesListTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type ListType = typeof(ReleasesList);

    public ReleasesListTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        SetupDefaults();
    }

    private void SetupDefaults()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items = [new ProjectDto { Id = 1, Name = "MyProject", Status = ProjectStatus.Active }],
            TotalCount = 1
        });
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto>
        {
            Items =
            [
                new ReleaseDto { Id = 1, ProjectId = 1, ProjectName = "MyProject", Version = "1.0.0", Status = ReleaseStatus.Published },
                new ReleaseDto { Id = 2, ProjectId = 1, ProjectName = "MyProject", Version = "1.1.0", Status = ReleaseStatus.Detected }
            ],
            TotalCount = 2
        });
    }

    // --- Render ---

    [Fact]
    public void GlobalScope_RendersReleases()
    {
        var cut = Render<ReleasesList>();
        cut.WaitForState(() => cut.Markup.Contains("1.0.0"), TimeSpan.FromSeconds(2));
        Assert.Contains("1.0.0", cut.Markup);
    }

    [Fact]
    public void ProjectScope_RendersReleases_WithoutProjectColumn()
    {
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto>
        {
            Items = [new ReleaseDto { Id = 3, ProjectId = 7, ProjectName = "Scoped", Version = "2.1.0", Status = ReleaseStatus.Published }],
            TotalCount = 1
        });

        var cut = Render<ReleasesList>(p => p.Add(x => x.ProjectId, 7));
        cut.WaitForState(() => cut.Markup.Contains("2.1.0"), TimeSpan.FromSeconds(2));

        Assert.Contains("2.1.0", cut.Markup);
        // Project column is hidden in project scope, so the project name must not appear as a cell.
        Assert.DoesNotContain("Scoped", cut.Markup);
    }

    [Fact]
    public void GlobalScope_EmptyReleases_DoesNotThrow()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto> { Items = [], TotalCount = 0 });
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto> { Items = [], TotalCount = 0 });

        var cut = Render<ReleasesList>();
        cut.WaitForState(() => _handler.Requests.Any(r => r.Url.Contains("api/releases")), TimeSpan.FromSeconds(2));

        // Global scope loads its project filter list + releases even when empty; the empty result must
        // not surface any release version row.
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/projects"));
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/releases"));
        Assert.DoesNotContain("1.0.0", cut.Markup);
    }

    // --- Behavior (reflection on the component) ---

    [Fact]
    public async Task OnLoadData_LoadsReleases()
    {
        var cut = Render<ReleasesList>();
        var method = ListType.GetMethod("OnLoadData", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [new LoadDataArgs()])!);

        var releases = (List<ReleaseDto>)ListType.GetField("_releases", Priv)!.GetValue(cut.Instance)!;
        Assert.NotEmpty(releases);
        Assert.False((bool)ListType.GetField("_loading", Priv)!.GetValue(cut.Instance)!);
    }

    [Fact]
    public async Task OnLoadData_WithSkipAndTop_PagesWithoutError()
    {
        var cut = Render<ReleasesList>();
        var method = ListType.GetMethod("OnLoadData", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [new LoadDataArgs { Skip = 25, Top = 25 }])!);

        Assert.False((bool)ListType.GetField("_loading", Priv)!.GetValue(cut.Instance)!);
    }

    [Fact]
    public async Task ReleaseCreatedOutsideProjectScope_IsIgnored()
    {
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto>
        {
            Items = [new ReleaseDto { Id = 1, ProjectId = 7, Version = "inside" }],
            TotalCount = 1
        });
        var cut = Render<ReleasesList>(parameters => parameters.Add(component => component.ProjectId, 7));
        var method = ListType.GetMethod("OnReleaseCreated", Priv)!;

        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance,
            [new ReleaseDto { Id = 99, ProjectId = 8, Version = "outside" }])!);

        var releases = (List<ReleaseDto>)ListType.GetField("_releases", Priv)!.GetValue(cut.Instance)!;
        Assert.DoesNotContain(releases, release => release.Id == 99);
    }

    [Fact]
    public async Task SyncReleases_WithNoFilter_DoesNothing()
    {
        var cut = Render<ReleasesList>();
        ListType.GetField("_projectFilter", Priv)!.SetValue(cut.Instance, null);
        var requestsBeforeSync = _handler.Requests.Count;
        var method = ListType.GetMethod("SyncReleases", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);

        Assert.False((bool)ListType.GetField("_syncing", Priv)!.GetValue(cut.Instance)!);
        Assert.Equal(requestsBeforeSync, _handler.Requests.Count);
        Assert.DoesNotContain(_handler.Requests, request =>
            request.Method == HttpMethod.Post.Method && request.Url.Contains("api/releases/sync/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SyncReleases_WithFilter_NewReleases_Completes()
    {
        const string syncUrl = "api/releases/sync/1";
        const string reloadUrl = "api/releases?page=1&pageSize=25&projectId=1";
        _handler.SetJsonResponse(HttpMethod.Post, syncUrl, new List<ReleaseDto>
        {
            new() { Id = 9, ProjectId = 1, Version = "1.2.0", Status = ReleaseStatus.Detected }
        });
        var cut = Render<ReleasesList>();
        cut.WaitForState(() => cut.Markup.Contains("1.0.0", StringComparison.Ordinal));
        var releaseGetsBeforeSync = _handler.Requests.Count(request =>
            request.Method == HttpMethod.Get.Method && request.Url.Contains("api/releases?", StringComparison.Ordinal));
        _handler.SetJsonResponse(HttpMethod.Get, reloadUrl, new PaginatedResult<ReleaseDto>
        {
            Items = [new ReleaseDto { Id = 9, ProjectId = 1, ProjectName = "MyProject", Version = "1.2.0", Status = ReleaseStatus.Detected }],
            TotalCount = 1,
            Page = 1,
            PageSize = 25
        });
        ListType.GetField("_projectFilter", Priv)!.SetValue(cut.Instance, 1);
        var method = ListType.GetMethod("SyncReleases", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);

        Assert.False((bool)ListType.GetField("_syncing", Priv)!.GetValue(cut.Instance)!);
        Assert.Single(_handler.Requests, request =>
            request.Method == HttpMethod.Post.Method && request.Url == $"http://test/{syncUrl}");
        Assert.True(_handler.Requests.Count(request =>
            request.Method == HttpMethod.Get.Method && request.Url.Contains("api/releases?", StringComparison.Ordinal))
            > releaseGetsBeforeSync);
        cut.WaitForAssertion(() => Assert.Contains("1.2.0", cut.Markup, StringComparison.Ordinal));
        Assert.Contains(Services.GetRequiredService<NotificationService>().Messages, message =>
            message.Severity == NotificationSeverity.Success
            && message.Summary == "Synced"
            && message.Detail.Contains("ReleasesSynced", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ClearFilters_ResetsProjectFilter()
    {
        var cut = Render<ReleasesList>();
        ListType.GetField("_projectFilter", Priv)!.SetValue(cut.Instance, 1);
        var method = ListType.GetMethod("ClearFilters", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);

        Assert.Null((int?)ListType.GetField("_projectFilter", Priv)!.GetValue(cut.Instance));
    }

    [Fact]
    public void RefreshCanWrite_SetsFlag()
    {
        var cut = Render<ReleasesList>();
        ListType.GetMethod("RefreshCanWrite", Priv)!.Invoke(cut.Instance, []);

        Assert.True((bool)ListType.GetField("_canWrite", Priv)!.GetValue(cut.Instance)!);
    }

    [Fact]
    public void OnPermissionsChanged_UpdatesCanWrite()
    {
        var cut = Render<ReleasesList>();
        ListType.GetField("_canWrite", Priv)!.SetValue(cut.Instance, false);
        ListType.GetMethod("OnPermissionsChanged", Priv)!.Invoke(cut.Instance, []);

        Assert.True((bool)ListType.GetField("_canWrite", Priv)!.GetValue(cut.Instance)!);
    }

    [Fact]
    public async Task TriggerBuild_NoPipelines_DoesNotThrow()
    {
        _handler.SetJsonResponse("api/pipelines", new PaginatedResult<PipelineDto> { Items = [], TotalCount = 0 });
        var cut = Render<ReleasesList>();
        var release = new ReleaseDto { Id = 1, Version = "1.0.0", Status = ReleaseStatus.Detected, ProjectName = "App" };
        var method = ListType.GetMethod("TriggerBuild", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [release])!);

        // TriggerBuild fetches the candidate pipelines first; with none it warns and returns before
        // opening the select dialog or POSTing a build trigger.
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/pipelines"));
        Assert.DoesNotContain(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("/build"));
    }

    [Fact]
    public async Task DisposeAsync_Completes()
    {
        var cut = Render<ReleasesList>();
        await cut.InvokeAsync(() => cut.Instance.DisposeAsync().AsTask());
    }

    // --- S-TECH-B4H9: realtime wiring (#8) - project scope reuses ProjectDetailLoader.OnChanged ---

    [Fact]
    public void ProjectScope_SubscribesToProjectLoaderOnChanged()
    {
        var loader = Services.GetRequiredService<ProjectDetailLoader>();
        // No subscriber before any project-scoped list mounts.
        Assert.Null(GetOnChangedHandler(loader));

        var cut = Render<ReleasesList>(p => p.Add(x => x.ProjectId, 7));
        cut.WaitForState(() => cut.Markup.Contains("1.0.0") || cut.Markup.Contains("NoReleasesFound"), TimeSpan.FromSeconds(2));

        // Project scope subscribed to the loader's OnChanged (the reused releases hub path).
        Assert.NotNull(GetOnChangedHandler(loader));
    }

    [Fact]
    public async Task ProjectScope_ProjectLoaderOnChanged_ReloadsGridWithNewData()
    {
        var cut = Render<ReleasesList>(p => p.Add(x => x.ProjectId, 7));
        cut.WaitForState(() => cut.Markup.Contains("1.0.0"), TimeSpan.FromSeconds(2));

        // A new release arrives; raising the loader's OnChanged must re-page the grid and surface it.
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto>
        {
            Items = [new ReleaseDto { Id = 50, ProjectId = 7, Version = "9.9.9", Status = ReleaseStatus.Published }],
            TotalCount = 1
        });
        var handler = GetOnChangedHandler(Services.GetRequiredService<ProjectDetailLoader>());
        Assert.NotNull(handler);
        await cut.InvokeAsync(() => handler!.Invoke());

        cut.WaitForState(() => cut.Markup.Contains("9.9.9"), TimeSpan.FromSeconds(2));
        Assert.Contains("9.9.9", cut.Markup);
    }

    private static Action? GetOnChangedHandler(ProjectDetailLoader loader) =>
        (Action?)typeof(ProjectDetailLoader).GetField("OnChanged", Priv)!.GetValue(loader);
}
