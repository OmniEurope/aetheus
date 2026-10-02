// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers.AgentWizard;

public partial class WizardCodeBlock
{
    [Parameter] public string Code { get; set; } = string.Empty;
    [Parameter] public string? Label { get; set; }
    [Parameter] public bool InlineLine { get; set; }

    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    // OmniCodeBlock copies and shows its own check; the toast keeps the app-wide copy feedback.
    private void OnCopied(bool copied) => WizardCopyFeedback.Report(Toast, L, copied);
}
