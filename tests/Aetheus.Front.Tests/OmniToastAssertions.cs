// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.DependencyInjection;
using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Tests;

/// <summary>
/// Reads the toasts raised through NotifyHelper back from the real <see cref="OmniOverlayService"/>
/// (PLAN-008 lot 11). NotifyHelper puts the summary in the notification's title and the detail in its
/// message, or, when there is no detail, the summary in the message and no title; this undoes that
/// mapping so a test states the summary and detail it expects.
/// </summary>
internal static class OmniToastAssertions
{
    public static IReadOnlyList<RaisedToast> Toasts(this OmniOverlayService overlay) =>
        [.. overlay.Notifications.Select(notification => notification.Title is null
            ? new RaisedToast(notification.Severity, notification.Message, string.Empty)
            : new RaisedToast(notification.Severity, notification.Title, notification.Message))];

    public static IReadOnlyList<RaisedToast> Toasts(this IServiceProvider services) =>
        services.GetRequiredService<OmniOverlayService>().Toasts();

    /// <summary>Dismisses every toast, as a reader closing them would.</summary>
    public static void ClearToasts(this IServiceProvider services)
    {
        var overlay = services.GetRequiredService<OmniOverlayService>();
        foreach (var notification in overlay.Notifications.ToList())
            overlay.Dismiss(notification.Id);
    }
}
