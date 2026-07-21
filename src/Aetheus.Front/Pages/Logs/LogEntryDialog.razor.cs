// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Logs;

public partial class LogEntryDialog : ComponentBase
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private ClipboardService Clipboard { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;

    [Parameter] public string Message { get; set; } = string.Empty;
    [Parameter] public string? Exception { get; set; }

    private Task CopyAsync()
    {
        var text = string.IsNullOrWhiteSpace(Exception) ? Message : $"{Message}\n\n{Exception}";
        return Clipboard.CopyAsync(text, L["MessageCopied"]);
    }

    private void Close() => Dialog.Close();
}
