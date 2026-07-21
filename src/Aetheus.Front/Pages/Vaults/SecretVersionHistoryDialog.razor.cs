// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Vaults;

public partial class SecretVersionHistoryDialog
{
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public List<VaultSecretVersionDto> Versions { get; set; } = [];

    private static BadgeStyle GetBadge(ChangeType type) => type switch
    {
        ChangeType.Created => BadgeStyle.Success,
        ChangeType.Updated => BadgeStyle.Info,
        ChangeType.Deleted => BadgeStyle.Danger,
        _ => BadgeStyle.Light
    };

    private void Close() => Dialog.Close();
}
