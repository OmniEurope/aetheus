// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Shared;

public partial class MonacoDiffViewer : IAsyncDisposable
{
    [Inject] private IJSRuntime Js { get; set; } = default!;

    [Parameter] public string OriginalValue { get; set; } = string.Empty;
    [Parameter] public string ModifiedValue { get; set; } = string.Empty;
    [Parameter] public bool IsDark { get; set; } = true;

    private string ElementId { get; } = $"monaco-diff-{Guid.NewGuid():N}";
    private bool _initialized;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await Js.InvokeVoidAsync("monacoInterop.initDiffEditor", ElementId, OriginalValue, ModifiedValue, IsDark);
            _initialized = true;
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_initialized)
        {
            await Js.InvokeVoidAsync("monacoInterop.updateDiffEditor", ElementId, OriginalValue, ModifiedValue);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_initialized)
        {
            await Js.InvokeVoidAsync("monacoInterop.disposeDiffEditor", ElementId);
        }
    }
}
