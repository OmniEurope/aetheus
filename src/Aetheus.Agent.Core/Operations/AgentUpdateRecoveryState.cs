// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// Local handshake between the detached rollback guard and the freshly started agent.
/// The guard restores the bounded rollback snapshot unless a successful backend heartbeat
/// removes the marker before the confirmation deadline.
/// </summary>
internal static class AgentUpdateRecoveryState
{
    internal const string MarkerFileName = ".agent-update-pending";

    internal static string GetMarkerPath(string workDirectory) =>
        Path.Combine(workDirectory, MarkerFileName);

    internal static void MarkPending(string workDirectory) =>
        WriteProcessId(workDirectory, 0);

    internal static void RegisterStartedProcess(string workDirectory)
    {
        var markerPath = GetMarkerPath(workDirectory);
        if (File.Exists(markerPath))
            WriteProcessId(workDirectory, Environment.ProcessId);
    }

    internal static void ConfirmSuccessfulHeartbeat(string workDirectory)
    {
        var markerPath = GetMarkerPath(workDirectory);
        if (File.Exists(markerPath))
            File.Delete(markerPath);
    }

    private static void WriteProcessId(string workDirectory, int processId)
    {
        Directory.CreateDirectory(workDirectory);
        var markerPath = GetMarkerPath(workDirectory);
        var temporaryPath = markerPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            processId.ToString(CultureInfo.InvariantCulture));
        File.Move(temporaryPath, markerPath, overwrite: true);
    }
}
