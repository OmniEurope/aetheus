// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Shared;

public enum ToggleKind
{
    Checkbox,
    Switch
}

/// <summary>
/// A Radzen checkbox/switch paired with a genuinely clickable text label. Radzen's own
/// <c>RadzenLabel Component=</c> only associates the label for accessibility (focuses text inputs)
/// but does NOT toggle a checkbox/switch when the label text is clicked - Radzen drives the toggle
/// from an internal element the <c>for</c> association never reaches. This wraps the control in a
/// native <c>&lt;label&gt;</c> associated with a visually-hidden native checkbox, so clicking anywhere
/// on the row and the browser's native Space-key behaviour both toggle it without scrolling. The
/// inner Radzen control is display-only (bound one-way to <see cref="Value"/>).
/// </summary>
public partial class LabeledToggle
{
    /// <summary>Current value (one-way into the control; changes flow back via <see cref="ValueChanged"/>).</summary>
    [Parameter] public bool Value { get; set; }

    [Parameter] public EventCallback<bool> ValueChanged { get; set; }

    [Parameter] public string Text { get; set; } = string.Empty;

    [Parameter] public ToggleKind Kind { get; set; } = ToggleKind.Checkbox;

    /// <summary>Render the text before the control (e.g. a label-left / switch-right row).</summary>
    [Parameter] public bool TextFirst { get; set; }

    [Parameter] public bool Disabled { get; set; }

    /// <summary>Optional Material Symbol shown between the control and the text.</summary>
    [Parameter] public string? Icon { get; set; }

    /// <summary>Extra CSS class forwarded to the wrapping label (e.g. margins).</summary>
    [Parameter] public string? Class { get; set; }

    private async Task OnNativeChangeAsync(ChangeEventArgs e)
    {
        if (Disabled)
            return;

        await ValueChanged.InvokeAsync(e.Value is bool isChecked ? isChecked : !Value);
    }
}
