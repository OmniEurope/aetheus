// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerLogsSectionTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerLogsSectionTests() => _handler = BunitTestHelper.RegisterServices(this);

    private PaginatedResult<TaskLogDto> MakeLogPage(params TaskLogDto[] items) =>
        new() { Items = [.. items], TotalCount = items.Length };

    private IRenderedComponent<ServerLogsSection> RenderSection(int serverId = 1)
    {
        _handler.SetJsonResponse($"api/servers/{serverId}/logs", MakeLogPage(
            new TaskLogDto { Id = 1, Message = "Build started", Level = TaskLogLevel.Info },
            new TaskLogDto { Id = 2, Message = "Build failed", Level = TaskLogLevel.Error }
        ));
        return Render<ServerLogsSection>(p => p.Add(x => x.ServerId, serverId));
    }

    [Fact]
    public void Renders_WithLogs_OnInit()
    {
        var cut = RenderSection();
        cut.WaitForState(() => cut.Markup.Contains("Build started"), TimeSpan.FromSeconds(2));
        // OnInit GETs the first log page and renders the stubbed messages.
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/servers/1/logs"));
        Assert.Contains("Build failed", cut.Markup);
    }

    [Fact]
    public void ServerIdChange_ReloadsLogsOnSameInstance()
    {
        _handler.SetJsonResponse("api/servers/1/logs", MakeLogPage(
            new TaskLogDto { Id = 1, Message = "first-log", Level = TaskLogLevel.Info }));
        _handler.SetJsonResponse("api/servers/2/logs", MakeLogPage(
            new TaskLogDto { Id = 2, Message = "second-log", Level = TaskLogLevel.Info }));
        var cut = Render<ServerLogsSection>(p => p.Add(x => x.ServerId, 1));

        cut.Render(p => p.Add(x => x.ServerId, 2));

        cut.WaitForAssertion(() => Assert.Contains("second-log", cut.Markup));
        Assert.DoesNotContain("first-log", cut.Markup);
    }

    [Fact]
    public void Page_InitiallyOne()
    {
        var cut = RenderSection();
        var page = (int)typeof(ServerLogsSection).GetField("_page", Priv)!.GetValue(cut.Instance)!;
        Assert.Equal(1, page);
    }

    [Fact]
    public void Loading_InitiallyFalse()
    {
        var cut = RenderSection();
        var loading = (bool)typeof(ServerLogsSection).GetField("_loading", Priv)!.GetValue(cut.Instance)!;
        Assert.False(loading);
    }

    [Fact]
    public async Task LoadMoreAsync_IncrementsPageAndAppends()
    {
        var cut = RenderSection();
        cut.WaitForState(() =>
        {
            var r = typeof(ServerLogsSection).GetField("_result", Priv)!.GetValue(cut.Instance);
            return r is not null;
        }, TimeSpan.FromSeconds(2));

        // Stub page-2 response
        _handler.SetJsonResponse("api/servers/1/logs", MakeLogPage(
            new TaskLogDto { Id = 3, Message = "Deploy started", Level = TaskLogLevel.Info }
        ));

        var method = typeof(ServerLogsSection).GetMethod("LoadMoreAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        var page = (int)typeof(ServerLogsSection).GetField("_page", Priv)!.GetValue(cut.Instance)!;
        Assert.Equal(2, page);

        var result = (PaginatedResult<TaskLogDto>?)typeof(ServerLogsSection)
            .GetField("_result", Priv)!.GetValue(cut.Instance);
        Assert.NotNull(result);
        Assert.Equal(3, result!.Items.Count);
    }

    [Fact]
    public async Task LoadMoreAsync_SetsLoadingFalseAfterCompletion()
    {
        var cut = RenderSection();
        cut.WaitForState(() =>
        {
            var r = typeof(ServerLogsSection).GetField("_result", Priv)!.GetValue(cut.Instance);
            return r is not null;
        }, TimeSpan.FromSeconds(2));

        _handler.SetJsonResponse("api/servers/1/logs", MakeLogPage());

        var method = typeof(ServerLogsSection).GetMethod("LoadMoreAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        var loading = (bool)typeof(ServerLogsSection).GetField("_loading", Priv)!.GetValue(cut.Instance)!;
        Assert.False(loading);
    }

    [Fact]
    public async Task LoadMoreAsync_FailureKeepsPageForRetry()
    {
        var cut = RenderSection();
        cut.WaitForState(() => typeof(ServerLogsSection).GetField("_result", Priv)!.GetValue(cut.Instance) is not null);
        _handler.SetResponse(
            HttpMethod.Get,
            "page=2",
            System.Net.HttpStatusCode.InternalServerError);
        var method = typeof(ServerLogsSection).GetMethod("LoadMoreAsync", Priv)!;

        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        Assert.Equal(1, (int)typeof(ServerLogsSection).GetField("_page", Priv)!.GetValue(cut.Instance)!);
        Assert.True((bool)typeof(ServerLogsSection).GetField("_loadMoreError", Priv)!.GetValue(cut.Instance)!);
        _handler.SetJsonResponse(HttpMethod.Get, "page=2", MakeLogPage(
            new TaskLogDto { Id = 3, Message = "retry", Level = TaskLogLevel.Info }));

        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        Assert.Equal(2, (int)typeof(ServerLogsSection).GetField("_page", Priv)!.GetValue(cut.Instance)!);
        Assert.False((bool)typeof(ServerLogsSection).GetField("_loadMoreError", Priv)!.GetValue(cut.Instance)!);
        Assert.Equal(2, _handler.Requests.Count(request => request.Url.Contains("page=2", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(TaskLogLevel.Error, BadgeStyle.Danger)]
    [InlineData(TaskLogLevel.Warning, BadgeStyle.Warning)]
    [InlineData(TaskLogLevel.Info, BadgeStyle.Info)]
    public void GetLevelBadge_ReturnsExpected(TaskLogLevel level, BadgeStyle expected)
    {
        var method = typeof(ServerLogsSection).GetMethod("GetLevelBadge", PrivStatic)!;
        var result = (BadgeStyle)method.Invoke(null, [(object)level])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetLevelBadge_UnknownLevel_ReturnsLight()
    {
        var method = typeof(ServerLogsSection).GetMethod("GetLevelBadge", PrivStatic)!;
        var result = (BadgeStyle)method.Invoke(null, [(object)(TaskLogLevel)99])!;
        Assert.Equal(BadgeStyle.Light, result);
    }
}
