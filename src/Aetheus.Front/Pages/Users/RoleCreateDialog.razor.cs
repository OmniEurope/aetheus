// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Users;

public partial class RoleCreateDialog
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private readonly RoleFormModel _model = new();
    private bool _saving;
    private string? _error;

    private async Task OnSubmit()
    {
        _saving = true;
        _error = null;

        try
        {
            var created = await Api.CreateRoleAsync(new CreateRoleRequest
            {
                Name = _model.Name,
                Description = _model.Description
            });

            if (created is not null)
            {
                Dialog.Close(created);
                return;
            }

            _error = L["Error"].Value;
        }
        catch (HttpRequestException)
        {
            _error = L["Error"].Value;
        }
        _saving = false;
    }

    private void Cancel() => Dialog.Close(null);

    private sealed class RoleFormModel
    {
        [Required]
        [StringLength(50)]
        public string Name { get; set; } = string.Empty;

        [StringLength(200)]
        public string Description { get; set; } = string.Empty;
    }
}
