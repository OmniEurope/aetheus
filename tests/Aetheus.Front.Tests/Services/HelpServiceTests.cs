// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aetheus.Front.Services;

namespace Aetheus.Front.Tests;

public class HelpServiceTests
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static (HelpService svc, TestHandler handler) Create()
    {
        var handler = new TestHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        return (new HelpService(http), handler);
    }

    // --- ResolvePageKey ---

    [Theory]
    [InlineData("", "dashboard")]
    [InlineData("/", "dashboard")]
    [InlineData("servers", "servers")]
    [InlineData("/servers/", "servers")]
    [InlineData("servers/5", "servers")]
    [InlineData("projects", "projects")]
    [InlineData("projects/1", "projects")]
    [InlineData("artifacts", "artifacts")]
    [InlineData("artifacts/5", "artifacts")]
    // Project-scoped sub-section intentionally falls back to the "projects" article (like every
    // other /projects/{id}/... section), not the standalone artifacts article.
    [InlineData("projects/5/artifacts", "projects")]
    [InlineData("pipelines", "pipelines")]
    [InlineData("pipelines/runs", "pipelines")]
    [InlineData("pipelines/runs/5", "pipelines")]
    [InlineData("templates", "templates")]
    [InlineData("variable-libraries", "variable-libraries")]
    [InlineData("vaults", "vaults")]
    [InlineData("releases", "releases")]
    [InlineData("tasks", "tasks")]
    [InlineData("logs", "logs")]
    [InlineData("alerts", "alerts")]
    [InlineData("dashboards", "dashboards")]
    [InlineData("plugins", "plugins")]
    [InlineData("users", "users")]
    [InlineData("audit", "audit")]
    [InlineData("settings", "settings")]
    [InlineData("admin/dashboards", "dashboards")]
    [InlineData("admin/plugins", "plugins")]
    [InlineData("admin/users", "users")]
    [InlineData("admin/audit", "audit")]
    [InlineData("admin/api-reference", "api-reference")]
    [InlineData("admin/settings", "settings")]
    public void ResolvePageKey_KnownRoutes_ReturnsCorrectKey(string route, string expectedKey)
    {
        var (svc, _) = Create();
        Assert.Equal(expectedKey, svc.ResolvePageKey(route));
    }

    [Fact]
    public void ResolvePageKey_UnknownRoute_ReturnsNull()
    {
        var (svc, _) = Create();
        Assert.Null(svc.ResolvePageKey("unknown-page"));
    }

    // --- GetAllArticlesAsync / caching ---

    [Fact]
    public async Task GetAllArticlesAsync_ReturnsList()
    {
        var (svc, h) = Create();
        var articles = new List<HelpArticle>
        {
            new("servers", "Servers", "Manage servers", "rzi-server",
                [new HelpSection("Overview", "Server overview content")])
        };
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(articles, JsonOpts),
                System.Text.Encoding.UTF8, "application/json")
        };

        var result = await svc.GetAllArticlesAsync(Xunit.TestContext.Current.CancellationToken);
        Assert.Single(result);
        Assert.Equal("servers", result[0].Key);
    }

    [Fact]
    public async Task GetAllArticlesAsync_CachesResult()
    {
        var (svc, h) = Create();
        var articles = new List<HelpArticle> { new("k", "T", "D", "i", []) };
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(articles, JsonOpts),
                System.Text.Encoding.UTF8, "application/json")
        };

        var first = await svc.GetAllArticlesAsync(Xunit.TestContext.Current.CancellationToken);
        var second = await svc.GetAllArticlesAsync(Xunit.TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Equal(1, h.RequestCount);
    }

    [Fact]
    public async Task GetAllArticlesAsync_HttpError_ReturnsEmpty()
    {
        var (svc, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.NotFound);

        var result = await svc.GetAllArticlesAsync(Xunit.TestContext.Current.CancellationToken);
        Assert.Empty(result);
    }

    // --- GetArticleAsync ---

    [Fact]
    public async Task GetArticleAsync_Found_ReturnsArticle()
    {
        var (svc, h) = Create();
        var articles = new List<HelpArticle>
        {
            new("servers", "Servers", "Desc", "icon", []),
            new("projects", "Projects", "Desc2", "icon2", [])
        };
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(articles, JsonOpts),
                System.Text.Encoding.UTF8, "application/json")
        };

        var result = await svc.GetArticleAsync("projects", Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("Projects", result!.Title);
    }

    [Fact]
    public async Task GetArticleAsync_NotFound_ReturnsNull()
    {
        var (svc, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
        };

        Assert.Null(await svc.GetArticleAsync("nonexistent", Xunit.TestContext.Current.CancellationToken));
    }

    // --- GetArticleForRouteAsync ---

    [Fact]
    public async Task GetArticleForRouteAsync_KnownRoute_ReturnsArticle()
    {
        var (svc, h) = Create();
        var articles = new List<HelpArticle> { new("servers", "Servers", "Desc", "i", []) };
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(articles, JsonOpts),
                System.Text.Encoding.UTF8, "application/json")
        };

        var result = await svc.GetArticleForRouteAsync("servers/5", Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("Servers", result!.Title);
    }

    [Fact]
    public async Task GetArticleForRouteAsync_UnknownRoute_ReturnsNull()
    {
        var (svc, _) = Create();
        Assert.Null(await svc.GetArticleForRouteAsync("totally-unknown", Xunit.TestContext.Current.CancellationToken));
    }

    // --- SearchAsync ---

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsAllArticles()
    {
        var (svc, h) = Create();
        var articles = new List<HelpArticle>
        {
            new("a", "Alpha", "One", "i", []),
            new("b", "Beta", "Two", "i", [])
        };
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(articles, JsonOpts),
                System.Text.Encoding.UTF8, "application/json")
        };

        var result = await svc.SearchAsync("", Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task SearchAsync_MatchesTitle()
    {
        var (svc, h) = Create();
        var articles = new List<HelpArticle>
        {
            new("a", "Servers Guide", "Desc", "i", []),
            new("b", "Projects", "Desc", "i", [])
        };
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(articles, JsonOpts),
                System.Text.Encoding.UTF8, "application/json")
        };

        var result = await svc.SearchAsync("servers", Xunit.TestContext.Current.CancellationToken);
        Assert.Single(result);
        Assert.Equal("Servers Guide", result[0].Title);
    }

    [Fact]
    public async Task SearchAsync_MatchesDescription()
    {
        var (svc, h) = Create();
        var articles = new List<HelpArticle>
        {
            new("a", "Title", "Deploy your containers", "i", []),
            new("b", "Other", "Nothing relevant", "i", [])
        };
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(articles, JsonOpts),
                System.Text.Encoding.UTF8, "application/json")
        };

        var result = await svc.SearchAsync("containers", Xunit.TestContext.Current.CancellationToken);
        Assert.Single(result);
    }

    [Fact]
    public async Task SearchAsync_MatchesSectionContent()
    {
        var (svc, h) = Create();
        var articles = new List<HelpArticle>
        {
            new("a", "Title", "Desc", "i",
                [new HelpSection("Section1", "Configure docker networks")]),
            new("b", "Other", "Desc", "i", [])
        };
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(articles, JsonOpts),
                System.Text.Encoding.UTF8, "application/json")
        };

        var result = await svc.SearchAsync("docker", Xunit.TestContext.Current.CancellationToken);
        Assert.Single(result);
    }

    [Fact]
    public async Task SearchAsync_MatchesSectionTitle()
    {
        var (svc, h) = Create();
        var articles = new List<HelpArticle>
        {
            new("a", "Title", "Desc", "i",
                [new HelpSection("Docker Setup", "Some content")]),
            new("b", "Other", "Desc", "i", [])
        };
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(articles, JsonOpts),
                System.Text.Encoding.UTF8, "application/json")
        };

        var result = await svc.SearchAsync("docker", Xunit.TestContext.Current.CancellationToken);
        Assert.Single(result);
    }

    private sealed class TestHandler : HttpMessageHandler
    {
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestCount++;
            return Task.FromResult(Response);
        }
    }
}
