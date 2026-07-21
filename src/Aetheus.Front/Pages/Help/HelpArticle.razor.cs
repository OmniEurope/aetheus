// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Aetheus.Front.Pages.Help;

public partial class HelpArticle
{
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
        ["pipelines"] = "/pipelines",
        ["templates"] = "/templates",
        ["variable-libraries"] = "/variable-libraries",
        ["vaults"] = "/vaults",
        ["releases"] = "/releases",
        ["tasks"] = "/tasks",
        ["logs"] = "/logs",
        ["alerts"] = "/alerts",
        ["dashboards"] = "/dashboards",
        ["plugins"] = "/plugins",
        ["users"] = "/users",
        ["audit"] = "/audit",
        ["settings"] = "/settings",
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
}
