// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Teamspeak;

public enum TeamspeakAction
{
    Start,
    Stop,
    Restart,

    // Item #9 tier-1 - operator actions on connected clients. Reuse the same Action gateway
    // so existing infrastructure (audit, RBAC, hub broadcast) covers them with no special case.

    /// <summary>Moves a client to another channel (TS3 <c>clientmove clid=X cid=Y</c>).
    /// Less destructive than kick/ban; the most common admin action.</summary>
    MoveClient,

    /// <summary>Sends a popup ("poke") to a single client - distinct from the global broadcast
    /// already covered by <c>SendMessage</c>. TS3 <c>clientpoke clid=X msg=...</c>.</summary>
    PokeClient,

    /// <summary>Item #9 tier-2 - graceful daemon restart. The back composes: ServerQuery gm
    /// warning → delay (configurable seconds) → systemctl restart teamspeak3. Single button,
    /// minimal disruption to connected clients.</summary>
    GracefulRestart
}
