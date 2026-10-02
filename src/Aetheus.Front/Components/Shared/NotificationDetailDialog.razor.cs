// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

public partial class NotificationDetailDialog
{
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public string Detail { get; set; } = string.Empty;
    [Parameter] public string? CorrelationId { get; set; }

    /// <summary>Null until Copy is pressed; then what the browser actually did. Written in the dialog
    /// rather than raised as one more toast, and never "copied" when the browser refused.</summary>
    private bool? _copied;

    private async Task CopyAsync()
    {
        var text = string.IsNullOrWhiteSpace(CorrelationId) ? Detail : $"{Detail}\n{L["CorrelationId"]}: {CorrelationId}";
        _copied = await Js.InvokeAsync<bool>("Aetheus.copyToClipboard", text);
    }
}
