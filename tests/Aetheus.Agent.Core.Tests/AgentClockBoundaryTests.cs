// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;
using Microsoft.Extensions.Time.Testing;

namespace Aetheus.Agent.Core.Tests;

public sealed class AgentClockBoundaryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aetheus-clock-").FullName;

    [Fact]
    public void TokenExpiry_UsesInjectedClockAtExactBoundary()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(now);
        var state = new AgentState(time) { TokenExpiresAt = now.UtcDateTime.AddSeconds(1) };

        Assert.False(state.IsTokenExpired);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(state.IsTokenExpired);
    }

    [Fact]
    public void CacheTouch_UsesInjectedUtcClock()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 34, 56, TimeSpan.Zero);
        var directory = Directory.CreateDirectory(Path.Combine(_root, "cache")).FullName;

        ToolchainCacheManager.SetCurrentLastWriteTimeUtc(directory, new FakeTimeProvider(now));

        Assert.Equal(now.UtcDateTime, Directory.GetLastWriteTimeUtc(directory));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
