// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Components.Shared;

/// <summary>How a dialog opened through <see cref="AppDialogs"/> is laid out and dismissed.</summary>
public sealed record AppDialogOptions
{
    public AppDialogWidth Width { get; init; } = AppDialogWidth.Default;

    /// <summary>
    /// Whether a click on the backdrop closes the dialog. False by default, so a click
    /// beside a form does not throw its input away (PLAN-008 verification, E, G1). The close button
    /// and Escape still close it.
    /// </summary>
    public bool CloseOnBackdropClick { get; init; }

    /// <summary>
    /// Whether the reader can dismiss the dialog. True by default. False removes the close button and
    /// ignores Escape and the backdrop: the dialog closes only when its content calls
    /// <see cref="AppDialogs.Close"/> (a non-dismissible dialog).
    /// </summary>
    public bool Dismissible { get; init; } = true;

    public bool ShowClose { get; init; } = true;

    public bool Draggable { get; init; }

    public bool Resizable { get; init; }

    /// <summary>
    /// Recette R-406: what the dialog is for, given to OE's <see cref="OmniDialog.Intent"/> (the intention mark
    /// before the title; since OE 1.4.0 the header and the footer stay neutral, recette R2-029).
    /// <see cref="OmniTone.Accent"/> by default: a
    /// dialog opened with a component is a form or a detail; a question goes through
    /// <see cref="AppDialogs.ConfirmAsync"/>, where OE derives it from the action (warning when destructive).
    /// </summary>
    public OmniTone Intent { get; init; } = OmniTone.Accent;

    internal string? HeightClass { get; init; }
}
