// SPDX-License-Identifier: EUPL-1.2
using System;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

/// <summary>
/// Shared model bridging <see cref="ServerApacheSection"/> (the SignalR receiver) and
/// <see cref="ApacheConfigEditorDialog"/> (the DialogService-hosted form). The vhost config
/// text is fetched on the agent and pushed back over SignalR into the section's
/// <c>HandleTaskCompleted</c>; the section writes it here and raises <see cref="OnChanged"/>
/// so the open dialog can refresh. Public (not internal) because it is bound to a public
/// dialog <c>[Parameter]</c>.
/// </summary>
public sealed class ApacheConfigEditorModel
{
    public string SiteName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool Loading { get; set; }

    /// <summary>Raised when the section pushes new content/loading state from SignalR.</summary>
    public event Action? OnChanged;

    public void NotifyChanged() => OnChanged?.Invoke();
}
