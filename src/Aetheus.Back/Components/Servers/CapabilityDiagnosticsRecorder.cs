// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Components.Shared;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Servers;

/// <summary>
/// Persists the agent's capability diagnostics on the server row and logs them when they CHANGE, compared
/// with the value already stored. Recette R2-015: the log used to repeat the same warning on every beat
/// (every 30 s) for as long as a diagnostic lasted.
///
/// Two kinds are logged apart. A capability diagnostic (a sudoers drop-in present but unreadable, a
/// blocked passwordless sudo, a deployment grant whose functional probe failed) means a capability the
/// operator configured is OFF: Warning. A collection diagnostic (<see cref="HeartbeatCollectionDiagnostics"/>:
/// the beat overran its collection budget, a collector is still running from an earlier beat) only says
/// that beat reported cached data, and resolves on its own: Information.
/// </summary>
internal static class CapabilityDiagnosticsRecorder
{
    public static void Apply(Server server, IReadOnlyList<string> diagnostics, ILogger logger)
    {
        var json = diagnostics.Count == 0 ? null : JsonSerializer.Serialize(diagnostics);
        if (string.Equals(server.CapabilityDiagnosticsJson, json, StringComparison.Ordinal)) return;

        var previous = ServerDataMapper.DeserializeDiagnostics(server.CapabilityDiagnosticsJson);
        server.CapabilityDiagnosticsJson = json;

        var (capability, collection) = Split(diagnostics);
        var (previousCapability, previousCollection) = Split(previous);
        if (!capability.SequenceEqual(previousCapability, StringComparer.Ordinal))
        {
            if (capability.Count > 0)
                logger.LogWarning("Server {ServerId} capability diagnostics: {Diagnostics}",
                    server.Id, string.Join("; ", capability));
            else
                logger.LogInformation("Server {ServerId} capability diagnostics cleared", server.Id);
        }

        if (collection.Count > 0 && !collection.SequenceEqual(previousCollection, StringComparer.Ordinal))
            logger.LogInformation("Server {ServerId} heartbeat collection diagnostics: {Diagnostics}",
                server.Id, string.Join("; ", collection));
    }

    private static (List<string> Capability, List<string> Collection) Split(IEnumerable<string> diagnostics)
    {
        var capability = new List<string>();
        var collection = new List<string>();
        foreach (var diagnostic in diagnostics)
            (HeartbeatCollectionDiagnostics.IsCollectionDiagnostic(diagnostic) ? collection : capability).Add(diagnostic);
        return (capability, collection);
    }
}
