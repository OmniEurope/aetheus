// SPDX-License-Identifier: EUPL-1.2
using Markdig;

namespace Aetheus.Front.Pages.Help;

public partial class HelpArticle
{
    private static readonly MarkdownPipeline MarkdownPipeline =
        new MarkdownPipelineBuilder().DisableHtml().Build();

    [Parameter] public string PageKey { get; set; } = "";

    [Inject] private HelpService Help { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;

    private Services.HelpArticle? _article;
    private string? _pageRoute;

    private static readonly Dictionary<string, string> PageRoutes = new()
    {
        ["dashboard"] = "/",
        ["servers"] = "/servers",
        ["projects"] = "/projects",
        ["git-repositories"] = "/git-repositories",
        ["environments"] = "/environments",
        ["pipelines"] = "/pipelines",
        ["templates"] = "/templates",
        ["variable-libraries"] = "/variable-libraries",
        ["vaults"] = "/vaults",
        ["releases"] = "/releases",
        ["artifacts"] = "/projects",
        ["tasks"] = "/tasks",
        ["logs"] = "/logs",
        ["alerts"] = "/alerts",
        ["analysis"] = "/analysis",
        ["backups"] = "/backups",
        ["service-connections"] = "/service-connections",
        ["ai-tasks"] = "/ai-tasks",
        ["dashboards"] = "/dashboards",
        ["administration"] = "/admin",
        ["organizations"] = "/admin/organizations",
        ["roles"] = "/admin/roles",
        ["notification-rules"] = "/admin/notifications",
        ["ai-runner-profiles"] = "/admin/ai-profiles",
        ["package-feeds"] = "/admin/package-feeds",
        ["package-registry"] = "/admin/package-registry",
        ["plugins"] = "/plugins",
        ["users"] = "/users",
        ["audit"] = "/audit",
        ["system-logs"] = "/admin/system-logs",
        ["api-reference"] = "/api-reference",
        ["settings"] = "/settings",
        ["platform-settings"] = "/admin/settings",
    };

    protected override async Task OnParametersSetAsync()
    {
        _article = await Help.GetArticleAsync(PageKey);
        _pageRoute = PageRoutes.GetValueOrDefault(PageKey);

        if (_article is not null)
        {
            Breadcrumb.Set(
                new BreadcrumbItem(L["HelpCenter"], "/help"),
                new BreadcrumbItem(_article.Title)
            );
        }
        else
        {
            Nav.NavigateTo("/help", replace: true);
        }
    }

    private static string RenderContent(string content) =>
        Markdown.ToHtml(content, MarkdownPipeline);
}
