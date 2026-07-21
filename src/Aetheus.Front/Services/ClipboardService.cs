// SPDX-License-Identifier: EUPL-1.2
using Microsoft.JSInterop;
using Radzen;

namespace Aetheus.Front.Services;

/// <summary>
/// Single entry point for "copy to clipboard" across the app: one <c>navigator.clipboard.writeText</c>
/// call, one success toast, and a single graceful warning when the Clipboard API is unavailable
/// (insecure context / permission denied) instead of the per-call try/catch + silent
/// <c>Debug.WriteLine</c> that used to be duplicated at every copy button.
/// </summary>
public class ClipboardService(IJSRuntime js, NotifyHelper toast)
{
    /// <summary>
    /// Copies <paramref name="text"/> and shows a success toast. <paramref name="successDetail"/> is the
    /// already-resolved detail line (a localized string or the copied value); pass <c>null</c> for none.
    /// </summary>
    public async Task CopyAsync(string? text, string? successDetail = null)
    {
        try
        {
            await js.InvokeVoidAsync("navigator.clipboard.writeText", text ?? string.Empty);
            toast.Notify(NotificationSeverity.Success, "Copied", successDetail ?? string.Empty);
        }
        catch (Exception ex)
        {
            // Clipboard API unavailable (insecure context / denied permission) - warn once, never throw.
            toast.Warning("CopyFailed", "ClipboardUnavailable");
            System.Diagnostics.Debug.WriteLine($"[Clipboard] failed: {ex.Message}");
        }
    }
}
