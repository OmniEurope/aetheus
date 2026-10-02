// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Vaults;

public partial class SecretVersionHistoryDialog
{
    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public List<VaultSecretVersionDto> Versions { get; set; } = [];

    /// <summary>The date column shows local time, so its range filter reads the same instant.</summary>
    private static readonly Func<VaultSecretVersionDto, object?> LocalChangedAt = version => version.ChangedAt.ToLocalTime();

    private void Close() => Dialog.Close();
}
