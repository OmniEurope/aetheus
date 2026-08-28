// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components.Web;

namespace Aetheus.Front.Shared;

public partial class ConfirmDialog
{
    [Parameter] public bool Visible { get; set; }
    [Parameter] public string Title { get; set; } = string.Empty;
    [Parameter] public string Message { get; set; } = string.Empty;
    [Parameter] public string? ConfirmText { get; set; }
    [Parameter] public ButtonStyle ConfirmStyle { get; set; } = ButtonStyle.Danger;
    [Parameter] public bool IsBusy { get; set; }
    [Parameter] public EventCallback OnConfirm { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private ElementReference _overlayRef;
    private bool _focusPending;

    private Task OnConfirmClick() => OnConfirm.InvokeAsync();
    private Task OnCancelClick() => OnCancel.InvokeAsync();

    // Escape closes the dialog, matching WizardDialog/SecretValueDialog keyboard behaviour.
    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape" && !IsBusy)
            await OnCancelClick();
    }

    protected override void OnParametersSet() => _focusPending = Visible;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusPending)
        {
            _focusPending = false;
            await _overlayRef.FocusAsync();
        }
    }
}
