// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Pages.Servers.AgentWizard;

public partial class WizardStepSection
{
    [Parameter] public string Label { get; set; } = string.Empty;
    [Parameter] public IReadOnlyList<string> Lines { get; set; } = [];
}
