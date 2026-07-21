// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Aetheus.Front.Pages.Servers.AgentWizard;

public partial class WizardCodeBlock
{
    [Parameter] public string Code { get; set; } = string.Empty;
    [Parameter] public string? Label { get; set; }
    [Parameter] public bool InlineLine { get; set; }

    [Inject] private ClipboardService Clipboard { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private string OuterClass => InlineLine ? "wizard-code-line wizard-code-block" : "wizard-code-block";
    private string PreClass => InlineLine ? "wizard-pre wizard-line" : "wizard-pre";

    private Task CopyAsync() => Clipboard.CopyAsync(Code, L["CopiedToClipboard"]);
}
