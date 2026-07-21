// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Radzen;

namespace Aetheus.Front.Shared;

public partial class BrowserBackButton
{
    [Inject] private IJSRuntime Js { get; set; } = default!;

    [Parameter] public string? Text { get; set; }
    [Parameter] public string? Title { get; set; }
    [Parameter] public ButtonSize Size { get; set; } = ButtonSize.Medium;

    private async Task GoBackAsync() => await Js.InvokeVoidAsync("Aetheus.goBack");
}
