// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// Owns the authenticated session's global realtime lifecycle. Transport shutdown alone is not
/// sufficient: the services that own those transports also retain per-user projections, retry
/// loops and connection references that must be reset before another user can sign in within the
/// same WebAssembly runtime.
/// </summary>
public sealed class RealtimeSessionLifecycle(
    HubConnectionFactory hubFactory,
    TaskTrackerService taskTracker,
    AlertNotificationService alertNotifications,
    UserNotificationService userNotifications)
{
    /// <summary>
    /// Stops page and global transports while the current token is still available, then resets
    /// every global owner so its next <c>StartAsync</c> creates a fresh authenticated connection.
    /// </summary>
    public async Task StopAsync()
    {
        await hubFactory.StopAllAsync().ConfigureAwait(false);
        await taskTracker.StopAsync().ConfigureAwait(false);
        await alertNotifications.StopAsync().ConfigureAwait(false);
        await userNotifications.StopAsync().ConfigureAwait(false);
    }
}
