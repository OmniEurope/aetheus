// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers.AgentWizard;

public partial class WizardConfigureStep
{
    [Parameter] public string Platform { get; set; } = "linux";
    [Parameter] public string Token { get; set; } = "<TOKEN>";

    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private bool IsLinux => string.Equals(Platform, "linux", StringComparison.OrdinalIgnoreCase);
}
