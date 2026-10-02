// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Components.Servers.ServerDetailSections;

/// <summary>PLAN-005 lot 5: per-record result of a domain DNS verification (expected versus observed).</summary>
public partial class MailDnsCheckDialog
{
    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public MailDnsCheckDto Check { get; set; } = new();

}
