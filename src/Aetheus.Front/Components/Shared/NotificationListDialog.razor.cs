// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

public partial class NotificationListDialog
{
    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public IReadOnlyList<OmniNotificationMessage> Messages { get; set; } = [];

    private static OmniSeverity AlertSeverity(OmniSeverity severity) => severity switch
    {
        OmniSeverity.Success => OmniSeverity.Success,
        OmniSeverity.Warning => OmniSeverity.Warning,
        OmniSeverity.Danger => OmniSeverity.Danger,
        _ => OmniSeverity.Info
    };
}
