// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Shared;

public partial class DialogCloseButton
{
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    /// <summary>Optional label; defaults to the localized "Close".</summary>
    [Parameter] public string? Text { get; set; }
}
