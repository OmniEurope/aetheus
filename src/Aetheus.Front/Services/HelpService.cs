// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using System.Net.Http.Json;

namespace Aetheus.Front.Services;

public sealed class HelpService
{
    private readonly HttpClient _http;
    private readonly Dictionary<string, IReadOnlyList<HelpArticle>> _cache = [];

    public HelpService(HttpClient http)
    {
        _http = http;
    }

    public async Task<HelpArticle?> GetArticleForRouteAsync(string route, CancellationToken ct = default)
    {
        var pageKey = ResolvePageKey(route);
        if (pageKey is null)
            return null;

        return await GetArticleAsync(pageKey, ct).ConfigureAwait(false);
    }

    public string? ResolvePageKey(string route)
    {
        var path = route.Trim('/').ToLowerInvariant();

        if (string.IsNullOrEmpty(path))
            return "dashboard";

        foreach (var mapping in PageMappings)
        {
            if (path == mapping.Route || path.StartsWith(mapping.Prefix, StringComparison.Ordinal))
                return mapping.Key;
        }

        return null;
    }

    public async Task<HelpArticle?> GetArticleAsync(string pageKey, CancellationToken ct = default)
    {
        var articles = await GetAllArticlesAsync(ct).ConfigureAwait(false);
        return articles.FirstOrDefault(a => a.Key == pageKey);
    }

    public async Task<IReadOnlyList<HelpArticle>> GetAllArticlesAsync(CancellationToken ct = default)
    {
        var lang = GetLanguageKey();

        if (_cache.TryGetValue(lang, out var cached))
            return cached;

        var articles = await LoadArticlesAsync(lang, ct).ConfigureAwait(false);
        _cache[lang] = articles;
        return articles;
    }

    public async Task<IReadOnlyList<HelpArticle>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return await GetAllArticlesAsync(ct).ConfigureAwait(false);

        var articles = await GetAllArticlesAsync(ct).ConfigureAwait(false);
        return articles
            .Where(a => a.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || a.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || a.Sections.Any(s =>
                            s.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || s.Content.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private async Task<IReadOnlyList<HelpArticle>> LoadArticlesAsync(string lang, CancellationToken ct)
    {
        try
        {
            var articles = await _http.GetFromJsonAsync<List<HelpArticle>>(
                $"help/help-{lang}.json", JsonOptions.Web, ct).ConfigureAwait(false);
            return articles ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    private static string GetLanguageKey()
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        return culture.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? "fr" : "en";
    }

    private static readonly PageMapping[] PageMappings =
    [
        new("servers", "servers/", "servers"),
        new("projects", "projects/", "projects"),
        // The standalone artifact detail page (/artifacts/{id}) had a help article in both languages
        // but no mapping → it was orphaned (contextual help never routed). This routes only that
        // top-level page.
        // HLP7 (decided): project-scoped sub-sections (/projects/{id}/artifacts,
        // /projects/{id}/environments) deliberately KEEP the "projects" fallback rather than getting
        // per-suffix mappings. Rationale: (1) the "Projets" article is the umbrella for the whole
        // project-detail workspace, so every sub-section opening it is consistent and predictable;
        // (2) ResolvePageKey is prefix/equality-based (no per-id suffix matching), and "projects/"
        // already wins for any /projects/{id}/* route - a suffix override would mean special-casing
        // the matcher for marginal gain. The fallback is the intended behaviour, not a gap.
        new("artifacts", "artifacts/", "artifacts"),
        new("git-repositories", "git-repositories", "git-repositories"),
        new("environments", "environments/", "environments"),
        new("pipelines/runs", "pipelines/runs/", "pipelines"),
        new("pipelines", "pipelines/", "pipelines"),
        new("templates", "templates/", "templates"),
        new("variable-libraries", "variable-libraries/", "variable-libraries"),
        new("vaults", "vaults/", "vaults"),
        new("releases", "releases/", "releases"),
        new("tasks", "tasks/", "tasks"),
        new("logs", "logs/", "logs"),
        new("alerts", "alerts/", "alerts"),
        new("dashboards", "dashboards/", "dashboards"),
        new("plugins", "plugins/", "plugins"),
        new("admin/dashboards", "admin/dashboards", "dashboards"),
        new("admin/plugins", "admin/plugins", "plugins"),
        new("admin/organizations", "admin/organizations", "organizations"),
        new("admin/roles", "admin/roles", "roles"),
        new("admin/system-logs", "admin/system-logs", "system-logs"),
        new("admin/users", "admin/users", "users"),
        new("admin/audit", "admin/audit", "audit"),
        new("admin/api-reference", "admin/api-reference", "api-reference"),
        new("admin/settings", "admin/settings", "settings"),
        new("users", "users/", "users"),
        new("audit", "audit/", "audit"),
        new("api-reference", "api-reference", "api-reference"),
        new("settings", "settings/", "settings"),
    ];

    private sealed record PageMapping(string Route, string Prefix, string Key);
}

public sealed record HelpArticle(string Key, string Title, string Description, string Icon, IReadOnlyList<HelpSection> Sections);

public sealed record HelpSection(string Title, string Content);
