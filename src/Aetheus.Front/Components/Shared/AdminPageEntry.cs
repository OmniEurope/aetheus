// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// How an administration page opens: a caller who is not an administrator is sent home, an
/// administrator gets the "Administration / page" breadcrumb. One rule for every such page.
/// </summary>
internal static class AdminPageEntry
{
    /// <summary>False when the caller was sent away: the page must stop initializing.</summary>
    public static bool TryEnter(
        AuthStateProvider auth,
        NavigationManager navigation,
        BreadcrumbService breadcrumb,
        IStringLocalizer<AppStrings> text,
        string titleKey)
    {
        if (!auth.IsAdmin)
        {
            navigation.NavigateTo("/");
            return false;
        }

        breadcrumb.Set(
            new BreadcrumbItem(text["Administration"], "/admin"),
            new BreadcrumbItem(text[titleKey]));
        return true;
    }
}
