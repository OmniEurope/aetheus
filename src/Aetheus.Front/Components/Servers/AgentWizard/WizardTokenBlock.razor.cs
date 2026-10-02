// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers.AgentWizard;

public partial class WizardTokenBlock
{
    [Parameter] public string Token { get; set; } = string.Empty;

    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    // S-DES-15: the literal <TOKEN> placeholder shown before a real token is issued is not masked.
    private bool IsPlaceholder => Token.StartsWith('<');

    private void OnCopied(bool copied) => WizardCopyFeedback.Report(Toast, L, copied);
}
