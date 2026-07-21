// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Aetheus.Front.Shared;

/// <summary>
/// Blocking dialog shown when an admin changes the current user's roles, org membership, or the
/// permissions of a role they hold. It cannot be dismissed (no close button, no overlay/Esc close);
/// the single "Refresh" action forces a full page reload so the client re-bootstraps with the new
/// token (via refresh) and freshly fetched permissions.
/// </summary>
public partial class PermissionsChangedDialog
{
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private void Refresh() => Nav.NavigateTo(Nav.Uri, forceLoad: true);
}
