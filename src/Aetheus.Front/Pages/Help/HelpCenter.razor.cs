// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;

namespace Aetheus.Front.Pages.Help;

public partial class HelpCenter
{
    [Inject] private HelpService Help { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;

    private IReadOnlyList<Services.HelpArticle> _articles = [];
    private IReadOnlyList<Services.HelpArticle> _filtered = [];
    private string? _search;
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        Breadcrumb.Set(new BreadcrumbItem(L["HelpCenter"]));
        _articles = await Help.GetAllArticlesAsync();
        _filtered = _articles;
        _loading = false;
    }

    private async Task FilterAsync()
    {
        _filtered = await Help.SearchAsync(_search ?? "");
    }

    private void OnCardKeyDown(KeyboardEventArgs e, string articleKey)
    {
        if (e.Key is "Enter" or " ")
            Nav.NavigateTo($"/help/{articleKey}");
    }
}
