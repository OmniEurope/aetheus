// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Aetheus.WebAnalytics;

internal sealed class AnalyticsRequestRateLimiter(TimeProvider timeProvider)
{
    private const int AddressLimit = 60;
    private const int SiteLimit = 600;
    private const int MaxAddressPartitions = 10_000;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly object _sync = new();
    private readonly byte[] _addressKey = RandomNumberGenerator.GetBytes(32);
    private readonly Dictionary<string, WindowCounter> _addresses = new(StringComparer.Ordinal);
    private WindowCounter _site = new(DateTimeOffset.MinValue, 0);

    public bool TryAcquire(HttpContext context)
    {
        var now = timeProvider.GetUtcNow();
        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var addressHash = Convert.ToHexString(
                HMACSHA256.HashData(_addressKey, Encoding.UTF8.GetBytes(address)))
            .ToLowerInvariant();

        lock (_sync)
        {
            _site = Current(_site, now);
            if (_site.Count >= SiteLimit)
                return false;

            if (!_addresses.TryGetValue(addressHash, out var addressCounter))
            {
                if (_addresses.Count >= MaxAddressPartitions)
                {
                    foreach (var expired in _addresses
                                 .Where(item => now - item.Value.StartedAt >= Window)
                                 .Select(item => item.Key)
                                 .ToList())
                        _addresses.Remove(expired);
                    if (_addresses.Count >= MaxAddressPartitions)
                        return false;
                }
                addressCounter = new WindowCounter(now, 0);
            }

            addressCounter = Current(addressCounter, now);
            if (addressCounter.Count >= AddressLimit)
                return false;

            _addresses[addressHash] = addressCounter with { Count = addressCounter.Count + 1 };
            _site = _site with { Count = _site.Count + 1 };
            return true;
        }
    }

    private static WindowCounter Current(WindowCounter counter, DateTimeOffset now) =>
        now - counter.StartedAt >= Window ? new WindowCounter(now, 0) : counter;

    private sealed record WindowCounter(DateTimeOffset StartedAt, int Count);
}
