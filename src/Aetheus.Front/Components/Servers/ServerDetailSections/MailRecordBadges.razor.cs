// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Components.Servers.ServerDetailSections;

/// <summary>PLAN-005: state badges of a mail domain, account or alias (active, adopted, missing from the server).</summary>
public partial class MailRecordBadges
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public bool IsActive { get; set; }
    [Parameter] public MailRecordSource Source { get; set; }
    [Parameter] public DateTime? MissingSince { get; set; }
}
