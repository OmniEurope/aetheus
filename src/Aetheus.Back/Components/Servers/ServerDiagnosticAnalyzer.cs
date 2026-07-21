// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Servers;

/// <summary>
/// Diagnostic synthesis for "why is this server offline?". Reads cheap signals (last heartbeat,
/// token validity, agent version) and produces a single human-readable summary. Extracted from the
/// former <c>ServerService.Diagnostic.cs</c> partial.
/// </summary>
internal sealed class ServerDiagnosticAnalyzer(IServerRepository repo, TimeProvider timeProvider)
{
    // Lazy-evaluated once per process. The version of the running backend assembly is the wire-side
    // identifier operators correlate with releases.
    private static readonly string BackendVersionCached =
        typeof(ServerDiagnosticAnalyzer).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ServerDiagnosticAnalyzer).Assembly.GetName().Version?.ToString(3)
        ?? "unknown";

    public async Task<ServerDiagnosticDto?> DiagnoseAsync(int serverId, CancellationToken ct = default)
    {
        var server = await repo.FindServerWithTokensAsync(serverId, ct).ConfigureAwait(false);
        if (server is null) return null;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var hasEverReported = server.LastHeartbeat > DateTime.MinValue;
        double? secondsSince = hasEverReported
            ? Math.Max(0, (now - server.LastHeartbeat).TotalSeconds)
            : null;

        // The "active" token is the most-recently-issued one that is neither revoked nor in the past.
        // A renewed token leaves the old one in place (revoked=true), so MAX(CreatedAt) is what the
        // agent is actually using.
        var activeToken = server.Tokens
            .Where(t => !t.IsRevoked && t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefault();

        // If nothing active, fall back to the latest-issued (revoked or expired) so we can report
        // "token expired N days ago" rather than a blank silence.
        var referenceToken = activeToken ?? server.Tokens
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefault();

        double? daysRemaining = referenceToken is not null
            ? Math.Round((referenceToken.ExpiresAt - now).TotalDays, 1)
            : null;

        return new ServerDiagnosticDto
        {
            LastHeartbeatUtc = hasEverReported ? server.LastHeartbeat : null,
            SecondsSinceLastHeartbeat = secondsSince is null ? null : Math.Round(secondsSince.Value, 1),
            TokenValid = activeToken is not null,
            TokenExpiresAt = referenceToken?.ExpiresAt,
            TokenDaysRemaining = daysRemaining,
            AgentVersion = server.AgentVersion ?? string.Empty,
            BackendVersion = BackendVersionCached,
            // No hard breaks yet: anything that reported is considered compatible.
            // Wire a real semver gate here when the agent protocol changes.
            VersionsCompatible = hasEverReported ? true : null,
            Summary = BuildSummary(hasEverReported, secondsSince, activeToken is not null, daysRemaining,
                server.AgentVersion ?? string.Empty)
        };
    }

    private static string BuildSummary(bool hasEverReported, double? secondsSince, bool tokenValid, double? daysRemaining, string agentVersion)
    {
        // Order matters: token problems eat heartbeat problems (an expired token makes the agent
        // unable to talk at all, so reporting "no heartbeat" is a downstream effect - surface the cause).
        if (daysRemaining is { } d && d < 0)
            return $"Agent token expired {FormatSpan(TimeSpan.FromDays(-d))} ago - re-enroll the agent.";

        if (!tokenValid && daysRemaining is null)
            return "No agent token has ever been issued for this server - enroll the agent.";

        if (!hasEverReported)
            return "The agent has never reported. Verify the agent service is installed, enrolled and running.";

        if (secondsSince is { } s)
        {
            if (s < 60) return $"Healthy - last heartbeat {Math.Round(s)} s ago.";
            return $"No heartbeat for {FormatSpan(TimeSpan.FromSeconds(s))} - agent may be offline or unable to reach the backend.";
        }

        return string.IsNullOrEmpty(agentVersion)
            ? "No telemetry available."
            : $"Last known agent version: {agentVersion}.";
    }

    private static string FormatSpan(TimeSpan span)
    {
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays} d {span.Hours} h";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours} h {span.Minutes} m";
        if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes} m {span.Seconds} s";
        return $"{(int)span.TotalSeconds} s";
    }
}
