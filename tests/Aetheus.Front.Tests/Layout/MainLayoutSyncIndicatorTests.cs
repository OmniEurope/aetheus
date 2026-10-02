// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Layout;
using Bunit;

namespace Aetheus.Front.Tests.Layout;

/// <summary>
/// The layout hands its connection-lost overlay state to the synchronising notification, so the two are
/// never on screen together: the overlay stays the authority on a real outage.
/// </summary>
public sealed class MainLayoutSyncIndicatorTests : BunitContext
{
    public MainLayoutSyncIndicatorTests() => BunitTestHelper.RegisterServices(this);

    [Fact]
    public void SignedInLayout_RendersTheIndicator_AndPassesItTheOverlayState()
    {
        var cut = Render<MainLayout>();
        cut.WaitForAssertion(() => Assert.False(cut.FindComponent<RealtimeSyncIndicator>().Instance.OfflineOverlayShown),
            TimeSpan.FromSeconds(5));

        typeof(MainLayout).GetField("_showOfflineDialog", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(cut.Instance, true);
        cut.Render();

        Assert.True(cut.FindComponent<RealtimeSyncIndicator>().Instance.OfflineOverlayShown);
    }
}
