// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

public partial class PromotePipelineTemplateDialog : PipelineTemplatePromotionBase
{
    [Inject] private DialogService Dialog { get; set; } = default!;

    protected override Task OnInitializedAsync() => LoadPreviewAsync();

    private async Task SubmitAsync()
    {
        var result = await PromoteAsync();
        if (result is not null)
            Dialog.Close(true);
    }

    private void Cancel() => Dialog.Close(false);

}
