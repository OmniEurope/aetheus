// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Concurrent;
using Microsoft.Extensions.Primitives;

namespace Aetheus.Back.Services;

/// <summary>
/// Process-wide registry of per-user cache-eviction tokens, shared across every (scoped)
/// <see cref="ResourceAuthorizationService"/> instance. Registered as a SINGLETON on purpose: the
/// request that populates a user's authz cache and the (different) request that invalidates it run
/// in separate DI scopes, so a per-instance dictionary would never match across them - the
/// <c>authz:orgmember:*</c> entries would stay stranded until their TTL. The shared singleton makes
/// the eviction actually cross the scope boundary.
/// </summary>
public sealed class AuthzCacheEvictor
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tokens = new(StringComparer.Ordinal);

    /// <summary>An expiration token tying a cache entry to the user's current eviction generation.</summary>
    public CancellationChangeToken TokenFor(string username)
        => TokenFor(username, TokenLifetime);

    internal CancellationChangeToken TokenFor(string username, TimeSpan lifetime)
    {
        var cts = _tokens.GetOrAdd(username, key =>
        {
            var created = new CancellationTokenSource();
            created.Token.Register(() =>
                ((ICollection<KeyValuePair<string, CancellationTokenSource>>)_tokens)
                    .Remove(new KeyValuePair<string, CancellationTokenSource>(key, created)));
            created.CancelAfter(lifetime);
            return created;
        });
        return new CancellationChangeToken(cts.Token);
    }

    internal int TrackedUserCount => _tokens.Count;

    /// <summary>
    /// Evicts every cache entry tied to the user's token, then hands out a fresh token for later
    /// reads. The CTS is intentionally NOT disposed (a concurrent authz read may still be reading its
    /// <c>.Token</c>, and disposing would throw on that hot path); the GC reclaims it once no entry
    /// references it.
    /// </summary>
    public void Evict(string username)
    {
        if (!string.IsNullOrEmpty(username) && _tokens.TryRemove(username, out var cts))
            cts.Cancel();
    }
}
