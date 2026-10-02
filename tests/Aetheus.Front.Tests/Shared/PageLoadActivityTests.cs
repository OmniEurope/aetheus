// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Tests.Shared;

/// <summary>Recette R-333: when the page-load bar under the top bar shows and hides.</summary>
public sealed class PageLoadActivityTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(3);

    [Fact]
    public void RequestOutsideANavigation_IsNotCounted()
    {
        var activity = new PageLoadActivity();

        Assert.Null(activity.TrackStart());
        Assert.False(activity.IsActive);
    }

    [Fact]
    public async Task Navigation_StaysActiveWhileItsRequestRuns_ThenCloses()
    {
        var activity = new PageLoadActivity();
        activity.Begin();
        var window = activity.TrackStart();
        Assert.NotNull(window);

        // Well past the grace period: the pending request keeps the bar on.
        await Task.Delay(PageLoadActivity.Grace + TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        Assert.True(activity.IsActive);

        activity.TrackEnd(window.Value);
        await WaitUntilAsync(() => !activity.IsActive);
        Assert.False(activity.IsActive);
    }

    [Fact]
    public async Task NavigationWithoutRequests_ClosesAfterTheGracePeriod()
    {
        var activity = new PageLoadActivity();
        var changes = 0;
        activity.Changed += () => Interlocked.Increment(ref changes);

        activity.Begin();
        Assert.True(activity.IsActive);

        await WaitUntilAsync(() => !activity.IsActive);
        Assert.False(activity.IsActive);
        Assert.Equal(2, Volatile.Read(ref changes));
    }

    [Fact]
    public async Task ARequestOfAnOlderNavigation_DoesNotCloseTheNewOne()
    {
        var activity = new PageLoadActivity();
        activity.Begin();
        var oldWindow = activity.TrackStart()!.Value;
        activity.Begin();
        var newWindow = activity.TrackStart()!.Value;

        activity.TrackEnd(oldWindow);
        await Task.Delay(PageLoadActivity.Grace + TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        Assert.True(activity.IsActive);

        activity.TrackEnd(newWindow);
        await WaitUntilAsync(() => !activity.IsActive);
        Assert.False(activity.IsActive);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20, TestContext.Current.CancellationToken);
    }
}
