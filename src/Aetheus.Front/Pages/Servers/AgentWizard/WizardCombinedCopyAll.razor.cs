// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Servers.AgentWizard;

public partial class WizardCombinedCopyAll
{
    [Parameter] public string Combined { get; set; } = string.Empty;

    [Inject] private ClipboardService Clipboard { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    // WZC4: a dedicated message so the user knows it's the agent install command that landed on the
    // clipboard, not some other "Copied to clipboard" action.
    private Task CopyAllAsync() => Clipboard.CopyAsync(Combined, L["InstallCommandCopied"]);
}
