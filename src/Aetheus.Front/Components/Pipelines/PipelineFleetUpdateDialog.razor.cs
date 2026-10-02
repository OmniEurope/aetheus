// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

public partial class PipelineFleetUpdateDialog : PipelineFleetUpdateBase
{
    [Inject] private OmniDialogService Dialog { get; set; } = default!;

    protected override Task OnInitializedAsync() => LoadPreviewAsync();

    private async Task ConfirmAsync()
    {
        var updated = await ApplyUpdateAsync();
        if (updated is not null)
        {
            Toast.Success("Saved", "Saved");
            Dialog.Close(true);
        }
        else
            Toast.Error("Error", "SaveFailed");
    }

    private void Cancel() => Dialog.Close(false);
}
