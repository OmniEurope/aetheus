// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Aetheus.Front.Pages.Servers.AgentWizard;

public partial class WizardConfigureStep
{
    [Parameter] public string Platform { get; set; } = "linux";
    [Parameter] public string Token { get; set; } = "<TOKEN>";

    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private bool IsLinux => string.Equals(Platform, "linux", StringComparison.OrdinalIgnoreCase);

    // S-DES-15: true when no real token was issued yet and the literal <TOKEN> placeholder is shown,
    // so the UI can accent it as "replace me".
    private bool IsPlaceholderToken => Token.StartsWith('<');
}
