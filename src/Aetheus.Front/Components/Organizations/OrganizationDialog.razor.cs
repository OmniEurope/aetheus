// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Components.Organizations;
namespace Aetheus.Front.Components.Organizations;

public partial class OrganizationDialog
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Inject] private NotifyHelper Notify { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    /// <summary>Null for a creation dialog; the existing organization when editing.</summary>
    [Parameter] public OrganizationDto? Organization { get; set; }

    private bool _isNew => Organization is null;
    private bool _saving;
    private string? _error;
    private OrganizationFormModel _model = new();

    protected override void OnInitialized()
    {
        if (Organization is not null)
        {
            _model = new OrganizationFormModel
            {
                Name = Organization.Name,
                Slug = Organization.Slug,
                Description = Organization.Description
            };
        }
    }

    private async Task OnSubmit()
    {
        _saving = true;
        _error = null;

        try
        {
            OrganizationDto? result;
            if (_isNew)
            {
                result = await Api.Servers.CreateOrganizationAsync(_model.ToCreateRequest());
            }
            else
            {
                result = await Api.Servers.UpdateOrganizationAsync(Organization!.Id, _model.ToUpdateRequest());
            }

            if (result is not null)
            {
                Notify.Success("Saved");
                Dialog.Close(result);
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
}
