// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Components.Shared;

public partial class PortVerdictBadge
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public PortCheckEntryDto Entry { get; set; } = default!;

    private OmniTone Variant => Entry.Verdict switch
    {
        PortCheckVerdict.UsedByThisProject => OmniTone.Success,
        PortCheckVerdict.Free => OmniTone.Success,
        PortCheckVerdict.ListeningUndeclared => OmniTone.Warning,
        _ => OmniTone.Danger
    };

    private string Text => Entry.Verdict switch
    {
        PortCheckVerdict.UsedByThisProject => L["PortVerdictUsedByThisProject"].Value,
        PortCheckVerdict.Free => L["PortVerdictFree"].Value,
        PortCheckVerdict.ListeningUndeclared => L["PortVerdictListeningUndeclared"].Value,
        _ => string.Format(CultureInfo.CurrentCulture, L["PortVerdictTakenBy"].Value,
            string.IsNullOrWhiteSpace(Entry.OwnerLabel) ? "?" : Entry.OwnerLabel)
    };

    /// <summary>
    /// Who holds the port: the declared owner, and what the last scan actually saw, when they are
    /// known. The badge stays one phrase and this is a hover away rather than a second column.
    /// </summary>
    private string? Title
    {
        get
        {
            var parts = new[] { Entry.IsFree ? null : Entry.OwnerLabel, Entry.ObservedHolder }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }
    }
}
