// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Layout;

public partial class ProjectDetailLayout : IDisposable
{
    protected override void OnInitialized() => Loader.OnChanged += OnLoaderChanged;

    private void OnLoaderChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => Loader.OnChanged -= OnLoaderChanged;
}
