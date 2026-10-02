// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

public partial class TextExportDialog
{
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;

    /// <summary>The full text being exported.</summary>
    [Parameter, EditorRequired] public string Text { get; set; } = string.Empty;

    /// <summary>Name the downloaded file gets.</summary>
    [Parameter, EditorRequired] public string FileName { get; set; } = string.Empty;

    [Parameter] public string ContentType { get; set; } = "text/plain";

    private async Task CopyAsync()
    {
        // The helper answers false when the browser refused the write: "Copied" is only said when true.
        if (await Js.InvokeAsync<bool>("Aetheus.copyToClipboard", Text))
            Toast.Success("Copied");
        else
            Toast.Error("CopyFailed");
    }

    private Task DownloadAsync() =>
        Js.InvokeVoidAsync("downloadFile", FileName, Text, ContentType).AsTask();
}
