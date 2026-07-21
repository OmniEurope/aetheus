// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Shared;

// 0-a: the shared EnvironmentsList drives the global and project scopes (environments aren't
// server-scoped). Both are server-paginated via GetEnvironmentsAsync (projectId filter).
// Consolidates the coverage previously on the global page + project section. Type badge mapping is
// covered by EnvironmentHelperTests.
public class EnvironmentsListTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type ListType = typeof(EnvironmentsList);

    public EnvironmentsListTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void GlobalScope_RendersEnvironments()
    {
        _handler.SetJsonResponse("api/environments", new PaginatedResult<EnvironmentDto>
        {
            Items = [new EnvironmentDto { Id = 1, Name = "Prod", Type = EnvironmentType.Production }],
            TotalCount = 1
        });

        var cut = Render<EnvironmentsList>();
        cut.WaitForState(() => cut.Markup.Contains("Prod"), TimeSpan.FromSeconds(2));

        Assert.Contains("Prod", cut.Markup);
    }

    [Fact]
    public void ProjectScope_RendersEnvironments_WithoutProjectColumn()
    {
        _handler.SetJsonResponse("api/environments", new PaginatedResult<EnvironmentDto>
        {
            Items = [new EnvironmentDto { Id = 2, ProjectId = 7, ProjectName = "Scoped", Name = "ProjEnv", Type = EnvironmentType.Staging }],
            TotalCount = 1
        });

        var cut = Render<EnvironmentsList>(p => p.Add(x => x.ProjectId, 7));
        cut.WaitForState(() => cut.Markup.Contains("ProjEnv"), TimeSpan.FromSeconds(2));

        Assert.Contains("ProjEnv", cut.Markup);
        Assert.DoesNotContain("Scoped", cut.Markup);
    }

    [Fact]
    public async Task OnLoadData_LoadsEnvironments()
    {
        _handler.SetJsonResponse("api/environments", new PaginatedResult<EnvironmentDto>
        {
            Items = [new EnvironmentDto { Id = 1, Name = "Dev", Type = EnvironmentType.Development }],
            TotalCount = 1
        });
        var cut = Render<EnvironmentsList>();
        var method = ListType.GetMethod("OnLoadData", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [new LoadDataArgs()])!);

        var envs = (List<EnvironmentDto>)ListType.GetField("_environments", Priv)!.GetValue(cut.Instance)!;
        Assert.Single(envs);
    }

    [Fact]
    public async Task ClearFilters_ResetsSearch()
    {
        _handler.SetJsonResponse("api/environments", new PaginatedResult<EnvironmentDto> { Items = [], TotalCount = 0 });
        var cut = Render<EnvironmentsList>();
        ListType.GetField("_search", Priv)!.SetValue(cut.Instance, "abc");
        var method = ListType.GetMethod("ClearFilters", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);

        Assert.Null((string?)ListType.GetField("_search", Priv)!.GetValue(cut.Instance));
    }

    [Fact]
    public async Task DuplicateEnvironment_Success_Navigates()
    {
        _handler.SetJsonResponse("api/environments", new PaginatedResult<EnvironmentDto> { Items = [], TotalCount = 0 });
        _handler.SetJsonResponse("api/environments/1/duplicate", new EnvironmentDto { Id = 99, Name = "Copy" });
        var cut = Render<EnvironmentsList>();

        var method = ListType.GetMethod("DuplicateEnvironment", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [new EnvironmentDto { Id = 1, Name = "Prod" }])!);

        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        Assert.Contains("environments/99", nav.Uri);
    }

    [Fact]
    public async Task ProjectScope_NewEnvironment_NavigatesWithProjectId()
    {
        _handler.SetJsonResponse("api/environments", new PaginatedResult<EnvironmentDto> { Items = [], TotalCount = 0 });
        var cut = Render<EnvironmentsList>(p => p.Add(x => x.ProjectId, 42));

        var method = ListType.GetMethod("NewEnvironment", Priv)!;
        await cut.InvokeAsync(() => method.Invoke(cut.Instance, []));

        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        Assert.Contains("environments/new", nav.Uri);
        Assert.Contains("projectId=42", nav.Uri);
    }

    [Fact]
    public void OnPermissionsChanged_UpdatesCanWrite()
    {
        _handler.SetJsonResponse("api/environments", new PaginatedResult<EnvironmentDto> { Items = [], TotalCount = 0 });
        var cut = Render<EnvironmentsList>();
        ListType.GetField("_canWrite", Priv)!.SetValue(cut.Instance, false);
        ListType.GetMethod("OnPermissionsChanged", Priv)!.Invoke(cut.Instance, []);

        Assert.True((bool)ListType.GetField("_canWrite", Priv)!.GetValue(cut.Instance)!);
    }

    [Fact]
    public async Task DisposeAsync_Completes()
    {
        _handler.SetJsonResponse("api/environments", new PaginatedResult<EnvironmentDto> { Items = [], TotalCount = 0 });
        var cut = Render<EnvironmentsList>();

        await cut.InvokeAsync(() => cut.Instance.DisposeAsync().AsTask());
    }
}
