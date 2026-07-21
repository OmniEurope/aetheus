// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Projects.ProjectDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Projects;

public class ProjectLogsSectionTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private readonly BunitTestHelper.TestHandler _handler;

    public ProjectLogsSectionTests() => _handler = BunitTestHelper.RegisterServices(this);

    private PaginatedResult<TaskLogDto> MakeLogPage(params TaskLogDto[] items) =>
        new() { Items = [.. items], TotalCount = items.Length };

    private IRenderedComponent<ProjectLogsSection> RenderSection(int projectId = 1)
    {
        _handler.SetJsonResponse($"api/projects/{projectId}/logs", MakeLogPage(
            new TaskLogDto { Id = 1, Message = "Deploy started", Level = TaskLogLevel.Info },
            new TaskLogDto { Id = 2, Message = "Deploy failed", Level = TaskLogLevel.Error }
        ));
        return Render<ProjectLogsSection>(p => p.Add(x => x.ProjectId, projectId));
    }

    [Fact]
    public void Renders_WithLogs_OnInit()
    {
        var cut = RenderSection();
        cut.WaitForState(() => cut.Markup.Contains("Deploy started"), TimeSpan.FromSeconds(2));

        // OnInitializedAsync fetches page 1 of the project logs and renders the messages.
        Assert.Contains(_handler.Requests, r => r.Method == "GET" && r.Url.Contains("api/projects/1/logs"));
        Assert.Contains("Deploy started", cut.Markup);
        Assert.Contains("Deploy failed", cut.Markup);
    }

    [Fact]
    public void ProjectIdChange_ReloadsLogsOnSameInstance()
    {
        _handler.SetJsonResponse("api/projects/1/logs", MakeLogPage(
            new TaskLogDto { Id = 1, Message = "first-log", Level = TaskLogLevel.Info }));
        _handler.SetJsonResponse("api/projects/2/logs", MakeLogPage(
            new TaskLogDto { Id = 2, Message = "second-log", Level = TaskLogLevel.Info }));
        var cut = Render<ProjectLogsSection>(p => p.Add(x => x.ProjectId, 1));

        cut.Render(p => p.Add(x => x.ProjectId, 2));

        cut.WaitForAssertion(() => Assert.Contains("second-log", cut.Markup));
        Assert.DoesNotContain("first-log", cut.Markup);
    }

    [Fact]
    public void Page_InitiallyOne()
    {
        var cut = RenderSection();
        var page = (int)typeof(ProjectLogsSection).GetField("_page", Priv)!.GetValue(cut.Instance)!;
        Assert.Equal(1, page);
    }

    [Fact]
    public void Loading_InitiallyFalse()
    {
        var cut = RenderSection();
        var loading = (bool)typeof(ProjectLogsSection).GetField("_loading", Priv)!.GetValue(cut.Instance)!;
        Assert.False(loading);
    }

    [Fact]
    public async Task OnLoadDataAsync_LoadsRequestedPageWithoutAppending()
    {
        var cut = RenderSection();
        cut.WaitForState(() =>
        {
            var r = typeof(ProjectLogsSection).GetField("_result", Priv)!.GetValue(cut.Instance);
            return r is not null;
        }, TimeSpan.FromSeconds(2));

        _handler.SetJsonResponse(HttpMethod.Get, "page=2", MakeLogPage(
            new TaskLogDto { Id = 3, Message = "Rollback complete", Level = TaskLogLevel.Warning }
        ));

        var method = typeof(ProjectLogsSection).GetMethod("OnLoadDataAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance,
            [new LoadDataArgs { Skip = 25, Top = 25 }])!);

        var page = (int)typeof(ProjectLogsSection).GetField("_page", Priv)!.GetValue(cut.Instance)!;
        Assert.Equal(2, page);

        var result = (PaginatedResult<TaskLogDto>?)typeof(ProjectLogsSection)
            .GetField("_result", Priv)!.GetValue(cut.Instance);
        Assert.NotNull(result);
        Assert.Equal("Rollback complete", Assert.Single(result!.Items).Message);
        Assert.Contains(_handler.Requests, request => request.Url.Contains("page=2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnLoadDataAsync_SetsLoadingFalseAfterCompletion()
    {
        var cut = RenderSection();
        cut.WaitForState(() =>
        {
            var r = typeof(ProjectLogsSection).GetField("_result", Priv)!.GetValue(cut.Instance);
            return r is not null;
        }, TimeSpan.FromSeconds(2));

        _handler.SetJsonResponse(HttpMethod.Get, "page=2", MakeLogPage());

        var method = typeof(ProjectLogsSection).GetMethod("OnLoadDataAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance,
            [new LoadDataArgs { Skip = 25, Top = 25 }])!);

        var loading = (bool)typeof(ProjectLogsSection).GetField("_loading", Priv)!.GetValue(cut.Instance)!;
        Assert.False(loading);
    }

    [Theory]
    [InlineData(TaskLogLevel.Error, BadgeStyle.Danger)]
    [InlineData(TaskLogLevel.Warning, BadgeStyle.Warning)]
    [InlineData(TaskLogLevel.Info, BadgeStyle.Info)]
    public void GetLevelBadge_ReturnsExpected(TaskLogLevel level, BadgeStyle expected)
    {
        var method = typeof(ProjectLogsSection).GetMethod("GetLevelBadge", PrivStatic)!;
        var result = (BadgeStyle)method.Invoke(null, [(object)level])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetLevelBadge_UnknownLevel_ReturnsLight()
    {
        var method = typeof(ProjectLogsSection).GetMethod("GetLevelBadge", PrivStatic)!;
        var result = (BadgeStyle)method.Invoke(null, [(object)(TaskLogLevel)99])!;
        Assert.Equal(BadgeStyle.Light, result);
    }
}
