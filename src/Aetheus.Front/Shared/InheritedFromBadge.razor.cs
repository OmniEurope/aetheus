// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Shared;

public partial class InheritedFromBadge
{
    /// <summary>The label describing the owner type (e.g. "Project", "Environment", "Server").</summary>
    [Parameter, EditorRequired] public string OwnerLabel { get; set; } = string.Empty;

    /// <summary>The name of the owning entity. Badge is hidden when null/empty.</summary>
    [Parameter] public string? OwnerName { get; set; }
}
