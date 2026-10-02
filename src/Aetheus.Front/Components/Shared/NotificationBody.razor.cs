// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

public partial class NotificationBody
{
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public string Summary { get; set; } = string.Empty;
    [Parameter] public string Detail { get; set; } = string.Empty;
    [Parameter] public string? CorrelationId { get; set; }

    /// <summary>Admins get a link to the server log filtered on the correlation id.</summary>
    [Parameter] public string? LogsHref { get; set; }

    private ElementReference _text;
    private bool _clamped;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        try
        {
            _clamped = await Js.InvokeAsync<bool>("Aetheus.isClamped", _text);
        }
        catch (JSException)
        {
            // No measurement, no button: the four lines remain, which is the safe side.
            _clamped = false;
        }

        if (_clamped) StateHasChanged();
    }

    private Task OpenAsync() =>
        Dialog.OpenAsync<NotificationDetailDialog>(
            Summary,
            new Dictionary<string, object?>
            {
                [nameof(NotificationDetailDialog.Detail)] = Detail,
                [nameof(NotificationDetailDialog.CorrelationId)] = CorrelationId
            },
            new OmniDialogOptions { Width = "min(40rem, 96vw)", CloseDialogOnEsc = true, ShowClose = true, AutoFocusFirstElement = false });
}
