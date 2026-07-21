// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Shared;

public partial class DialogFooter
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    /// <summary>Cancel button label; defaults to the localized "Cancel".</summary>
    [Parameter] public string? CancelText { get; set; }

    /// <summary>Cancel button variant (most dialogs use Filled; a few use Text).</summary>
    [Parameter] public Variant CancelVariant { get; set; } = Variant.Filled;

    /// <summary>Invoked when the user clicks Cancel - typically closes the dialog.</summary>
    [Parameter] public EventCallback OnCancel { get; set; }

    /// <summary>The dialog's primary action button(s), right-aligned next to Cancel.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Optional secondary actions (Copy, artifact/pipeline links…) rendered left-aligned, opposite
    /// the Cancel + primary cluster. When present the footer switches to space-between layout.
    /// </summary>
    [Parameter] public RenderFragment? SecondaryContent { get; set; }
}
