// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Aetheus.Front.Pages.Servers.AgentWizard;

public partial class WizardVerifyStep
{
    [Parameter] public bool AgentFound { get; set; }
    [Parameter] public bool Listening { get; set; }
    [Parameter] public string? AgentName { get; set; }
    [Parameter] public string? HubError { get; set; }
    [Parameter] public EventCallback OnStart { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }
    [Parameter] public int ElapsedSeconds { get; set; }
    [Parameter] public int MaxSeconds { get; set; } = 120;
    [Parameter] public bool TimedOut { get; set; }
    [Parameter] public int? DetectedServerId { get; set; }

    private double Percent => MaxSeconds <= 0 ? 0 : Math.Min(100, ElapsedSeconds * 100.0 / MaxSeconds);
    private int RemainingSeconds => Math.Max(0, MaxSeconds - ElapsedSeconds);

    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
}
