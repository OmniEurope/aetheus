// SPDX-License-Identifier: EUPL-1.2

using System.Globalization;
using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Components.Shared;

public partial class CountBadge
{
    /// <summary>The count. Zero renders nothing at all.</summary>
    [Parameter] public int Value { get; set; }

    /// <summary>What the badge reads; defaults to the count itself, but a call site may format it
    /// ("3 total", "2 failed") without losing the zero rule.</summary>
    [Parameter] public string? Text { get; set; }

    /// <summary>Visual tone of the count.</summary>
    [Parameter] public OmniTone Variant { get; set; } = OmniTone.Neutral;

    [Parameter] public string? Class { get; set; }


    /// <summary>Resolved at render time, never cached in <see cref="Text"/>: Blazor leaves an unsupplied
    /// parameter untouched between renders, so writing the count into <see cref="Text"/> once would pin
    /// the badge to its first value while <see cref="Value"/> keeps changing (a filtered row count, an
    /// unread counter).</summary>
    private string DisplayText => Text ?? Value.ToString(CultureInfo.InvariantCulture);

    private OmniTone OmniVariant => Variant;
}
