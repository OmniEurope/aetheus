// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Logs;

public partial class LogEntryDialog : ComponentBase
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private ClipboardService Clipboard { get; set; } = default!;
    [Inject] private OmniDialogService Dialog { get; set; } = default!;

    [Parameter] public string Message { get; set; } = string.Empty;
    [Parameter] public string? Exception { get; set; }

    private Task CopyAsync()
    {
        var text = string.IsNullOrWhiteSpace(Exception) ? Message : $"{Message}\n\n{Exception}";
        return Clipboard.CopyAsync(text, L["MessageCopied"]);
    }

    private void Close() => Dialog.Close();
}
