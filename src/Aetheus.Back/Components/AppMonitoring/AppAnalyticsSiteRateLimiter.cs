// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Aetheus.Back.Configuration;

namespace Aetheus.Back.Components.AppMonitoring;

public sealed class AppAnalyticsSiteRateLimiter : IDisposable
{
    private readonly ConcurrentDictionary<int, FixedWindowRateLimiter> _limiters = new();

    public bool TryAcquire(int appId)
    {
        var limiter = _limiters.GetOrAdd(
            appId,
            _ => new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = 600,
                Window = BackendRuntimeDefaults.RateLimitWindow,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
        using var lease = limiter.AttemptAcquire();
        return lease.IsAcquired;
    }

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values)
            limiter.Dispose();
        _limiters.Clear();
    }
}
