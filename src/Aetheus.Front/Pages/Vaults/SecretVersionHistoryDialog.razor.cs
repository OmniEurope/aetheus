// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Vaults;

public partial class SecretVersionHistoryDialog
{
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public List<VaultSecretVersionDto> Versions { get; set; } = [];

    private void Close() => Dialog.Close();
}
