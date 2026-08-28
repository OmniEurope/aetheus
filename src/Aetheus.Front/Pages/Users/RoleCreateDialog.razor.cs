// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Users;

public partial class RoleCreateDialog
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
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
            var created = await Api.Auth.CreateRoleAsync(new CreateRoleRequest
            {
                Name = _model.Name,
                Description = _model.Description
            });

            if (created is not null)
            {
                Toast.Success("Created", "Saved");
                Dialog.Close(created);
                return;
            }

            _error = L["Error"].Value;
            Toast.Error("Error", "SaveFailed");
        }
        catch (HttpRequestException)
        {
            _error = L["Error"].Value;
            Toast.Error("Error", "SaveFailed");
        }
        _saving = false;
    }

    private void Cancel() => Dialog.Close(null);

}
