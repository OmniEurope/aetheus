// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Components.Servers;

/// <summary>
/// On-demand agent reachability. Agent communication is poll-based: the agent pushes a heartbeat
/// every <c>HeartbeatIntervalSeconds</c> (default 30s) and polls for tasks. There is no synchronous
/// server-&gt;agent channel, so the authoritative "is the agent talking to us" signal is heartbeat
/// freshness. Extracted from the former <c>ServerService.AgentContact.cs</c> partial.
/// </summary>
internal sealed class ServerAgentContactProbe(
    IServerRepository repo,
    IOptions<BackgroundServicesOptions> backgroundOptions,
    TimeProvider timeProvider)
{
    public async Task<ContactAgentResultDto?> ContactAgentAsync(int serverId, CancellationToken ct = default)
    {
        var server = await repo.FindServerAsync(serverId, ct).ConfigureAwait(false);
        if (server is null) return null;

        var freshnessWindow = backgroundOptions.Value.ServerHeartbeatTimeout;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // LastHeartbeat defaults to default(DateTime) when the agent has never reported.
        var hasEverReported = server.LastHeartbeat > DateTime.MinValue;
        double? secondsSince = hasEverReported
            ? Math.Max(0, (now - server.LastHeartbeat).TotalSeconds)
            : null;

        var reachable = hasEverReported && (now - server.LastHeartbeat) <= freshnessWindow;

        return new ContactAgentResultDto
        {
            Reachable = reachable,
            LastHeartbeat = hasEverReported ? server.LastHeartbeat : null,
            SecondsSinceLastHeartbeat = secondsSince is null ? null : Math.Round(secondsSince.Value, 1),
            AgentVersion = string.IsNullOrWhiteSpace(server.AgentVersion) ? null : server.AgentVersion,
            Error = reachable
                ? null
                : !hasEverReported
                    ? "The agent has never reported to the server. Verify the agent is installed, enrolled and running."
                    : $"No heartbeat received for {FormatDuration(secondsSince!.Value)} "
                      + $"(freshness window is {FormatDuration(freshnessWindow.TotalSeconds)}). "
                      + "The agent appears to be offline or unable to reach the server."
        };
    }

    private static string FormatDuration(double totalSeconds)
    {
        if (totalSeconds < 60) return $"{Math.Round(totalSeconds)}s";
        var span = TimeSpan.FromSeconds(totalSeconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : $"{span.Minutes}m {span.Seconds}s";
    }
}
