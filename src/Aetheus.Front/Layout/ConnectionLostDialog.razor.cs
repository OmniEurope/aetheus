// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Layout;

/// <summary>
/// Full-screen overlay shown when the realtime hub connection is lost. Presentational only: the
/// detection, grace period, countdown and manual-reconnect orchestration live in <c>MainLayout</c>,
/// which drives <c>OmniConnectionOverlay</c> through this component's parameters.
/// </summary>
public partial class ConnectionLostDialog
{
    /// <summary>Whether the overlay is displayed.</summary>
    [Parameter] public bool Visible { get; set; }

    /// <summary>Seconds until the next automatic reconnect attempt; hidden when 0.</summary>
    [Parameter] public int ReconnectCountdown { get; set; }

    /// <summary>True while a manual reconnect is in flight (spinner hidden, button busy).</summary>
    [Parameter] public bool ManualReconnecting { get; set; }

    /// <summary>
    /// Why the last attempt failed, when the tracker knows. Shown verbatim: the overlay saying only
    /// "connection lost" is what made a real incident impossible to describe without opening F12.
    /// </summary>
    [Parameter] public string? LastFailureReason { get; set; }

    /// <summary>Invoked when the user clicks the manual reconnect button.</summary>
    [Parameter] public EventCallback OnManualReconnect { get; set; }
}
