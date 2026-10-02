// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Microsoft.Extensions.Time.Testing;

namespace Aetheus.Front.Tests.Layout;

public class ReconnectCountdownTickerTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public async Task RunAsync_CountsDownSecondBySecond()
    {
        var ticker = new ReconnectCountdownTicker(_time);
        var ticks = new List<int>();

        var run = ticker.RunAsync(3, () => { ticks.Add(ticker.Remaining); return Task.CompletedTask; });

        Assert.Equal(3, ticks.Last());

        _time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(2, ticks.Last());

        _time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(1, ticks.Last());

        _time.Advance(TimeSpan.FromSeconds(1));
        await run;

        Assert.Equal(0, ticks.Last());
        Assert.Equal([3, 2, 1, 0], ticks);
    }

    [Fact]
    public async Task Stop_CancelsRunInFlight()
    {
        var ticker = new ReconnectCountdownTicker(_time);
        var ticks = new List<int>();

        var run = ticker.RunAsync(5, () => { ticks.Add(ticker.Remaining); return Task.CompletedTask; });

        _time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);

        ticker.Stop();
        await run;

        Assert.Equal(0, ticker.Remaining);
        Assert.DoesNotContain(0, ticks.Skip(1));
    }

    [Fact]
    public async Task SecondRun_SupersedesTheFirst()
    {
        var ticker = new ReconnectCountdownTicker(_time);
        var firstTicks = new List<int>();
        var secondTicks = new List<int>();

        var first = ticker.RunAsync(10, () => { firstTicks.Add(ticker.Remaining); return Task.CompletedTask; });

        _time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);

        var second = ticker.RunAsync(2, () => { secondTicks.Add(ticker.Remaining); return Task.CompletedTask; });
        await first;

        _time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(1));
        await second;

        Assert.Equal([2, 1, 0], secondTicks);
    }

    [Fact]
    public void Dispose_ClearsState()
    {
        var ticker = new ReconnectCountdownTicker(_time);
        _ = ticker.RunAsync(5, () => Task.CompletedTask);

        ticker.Dispose();

        Assert.Equal(0, ticker.Remaining);
    }
}
