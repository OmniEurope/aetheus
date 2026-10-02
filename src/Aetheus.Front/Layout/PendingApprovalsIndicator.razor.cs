// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Layout;

public partial class PendingApprovalsIndicator : IDisposable
{
    [Inject] private PendingApprovalsService Approvals { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private bool _popoverOpen;

    protected override async Task OnInitializedAsync()
    {
        Approvals.OnChanged += OnApprovalsChanged;
        await Approvals.EnsureStartedAsync();
    }

    private void OnApprovalsChanged() => InvokeAsync(StateHasChanged);

    public void Dispose() => Approvals.OnChanged -= OnApprovalsChanged;
}
