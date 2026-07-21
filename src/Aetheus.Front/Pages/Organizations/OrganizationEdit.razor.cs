// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs.Organizations;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Aetheus.Front.Pages.Organizations;

public partial class OrganizationEdit
{
    [Parameter] public int? Id { get; set; }
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private NotifyHelper Notify { get; set; } = default!;

    private bool _isNew => Id is null or 0;
    private bool _loading = true;
    private bool _saving;
    private CreateOrganizationRequest _model = new();

    protected override async Task OnInitializedAsync()
    {
        if (!Auth.IsAdmin)
        {
            Nav.NavigateTo("/");
            return;
        }

        // Editing now lives on the detail page's General tab. Keep this legacy route as a redirect
        // shim (a bookmarked /{id}/edit still lands on the right place); only /new renders the form.
        if (!_isNew && Id.HasValue)
        {
            Nav.NavigateTo($"/admin/organizations/{Id.Value}", replace: true);
            return;
        }

        Breadcrumb.Set(
            new BreadcrumbItem(L["Administration"], "/admin"),
            new BreadcrumbItem(L["Organizations"], "/admin/organizations"),
            new BreadcrumbItem(L["NewOrganization"]));

        _loading = false;
        await Task.CompletedTask;
    }

    private async Task OnSubmit()
    {
        _saving = true;
        try
        {
            var created = await Api.CreateOrganizationAsync(_model);
            if (created is not null)
            {
                Notify.Success(L["Saved"]);
                Nav.NavigateTo($"/admin/organizations/{created.Id}");
            }
        }
        finally
        {
            _saving = false;
        }
    }
}
