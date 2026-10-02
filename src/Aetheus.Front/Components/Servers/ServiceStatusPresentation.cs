// SPDX-License-Identifier: EUPL-1.2
using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Components.Servers;

/// <summary>
/// Recette R-507: what a service's raw status means to a reader. The agent reports what the host says
/// (systemd's sub-state or unit-file state, a Windows service state, "installed" for a binary found
/// without a unit); the page used to print that word as is, in red for anything but "running", so a
/// service that is simply stopped looked like a fault. Stopped is neutral, only a failure is red.
/// </summary>
internal static class ServiceStatusPresentation
{
    private static readonly HashSet<string> Starting = new(StringComparer.OrdinalIgnoreCase)
    {
        "activating", "auto-restart", "start", "start-pre", "start-post", "reload", "StartPending", "ContinuePending"
    };

    private static readonly HashSet<string> Stopping = new(StringComparer.OrdinalIgnoreCase)
    {
        "deactivating", "stop", "stop-sigterm", "stop-sigkill", "stop-post", "StopPending", "PausePending"
    };

    private static readonly HashSet<string> Stopped = new(StringComparer.OrdinalIgnoreCase)
    {
        "dead", "exited", "inactive", "stopped", "installed", "enabled", "enabled-runtime", "disabled", "static",
        "generated", "indirect", "alias", "transient", "linked", "linked-runtime", "paused"
    };

    /// <summary>The localization key of the status, or null for a word this page does not know, which
    /// is then shown as the host wrote it.</summary>
    internal static string? LabelKey(string status, bool isRunning)
    {
        if (isRunning || status.Equals("running", StringComparison.OrdinalIgnoreCase)) return "ServiceStatusRunning";
        if (status.Equals("failed", StringComparison.OrdinalIgnoreCase)) return "ServiceStatusFailed";
        if (status.Equals("scheduled", StringComparison.OrdinalIgnoreCase)) return "ServiceStatusScheduled";
        if (status.Equals("idle", StringComparison.OrdinalIgnoreCase)) return "ServiceStatusIdle";
        if (status.Equals("masked", StringComparison.OrdinalIgnoreCase)) return "ServiceStatusMasked";
        if (Starting.Contains(status)) return "ServiceStatusStarting";
        if (Stopping.Contains(status)) return "ServiceStatusStopping";
        return Stopped.Contains(status) ? "ServiceStatusStopped" : null;
    }

    internal static OmniTone Tone(string status, bool isRunning) => LabelKey(status, isRunning) switch
    {
        "ServiceStatusRunning" => OmniTone.Success,
        "ServiceStatusFailed" => OmniTone.Danger,
        "ServiceStatusScheduled" => OmniTone.Accent,
        "ServiceStatusStarting" or "ServiceStatusStopping" => OmniTone.Warning,
        _ => OmniTone.Neutral
    };
}
