// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Caching.Memory;

namespace Aetheus.Back.Components.Mail;

/// <summary>Cache keys of <see cref="CachedMailService"/>, shared with the heartbeat reconciliation so an
/// adopted row is visible on the next read instead of after the cache expiry.</summary>
internal static class MailCacheKeys
{
    public static string State(int serverId) => $"mail:state:{serverId}";
    public static string Domains(int serverId) => $"mail:domains:{serverId}";
    public static string Accounts(int serverId) => $"mail:accounts:{serverId}";

    public static void Invalidate(IMemoryCache cache, int serverId)
    {
        cache.Remove(State(serverId));
        cache.Remove(Domains(serverId));
        cache.Remove(Accounts(serverId));
    }
}
