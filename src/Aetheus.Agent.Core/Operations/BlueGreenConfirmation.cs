// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// PLAN-003 2.7, the agent side of the confirmation window. A deployment of the control plane itself
/// has a flaw no pipeline can cover: if the new backend is broken, nobody can approve its
/// confirmation and nothing can dispatch its Rollback stage, because that backend is what hands out
/// the tasks. So <c>bluegreen-switch</c> arms a countdown here, on the host, that only
/// <c>bluegreen-commit</c> (or a rollback) disarms; <see cref="BlueGreenConfirmationWatchdog"/> puts
/// traffic back on the previous colour by itself when it runs out.
///
/// One small file per environment under the agent's work directory, holding the deadline and the
/// blue-green variables the switch received - enough to rebuild the same context without the
/// backend. Those variables carry paths, ports and image tags, not secrets (the start-only names never
/// reach a switch), and the file is written owner-only anyway.
/// </summary>
internal static class BlueGreenConfirmation
{
    internal const string MinutesVariable = "AETHEUS_BG_CONFIRM_MINUTES";

    /// <summary>
    /// Added to the window before the agent acts. With a healthy backend the approval times out first
    /// and the pipeline's own Rollback stage runs; the agent only has to act when that did not happen.
    /// </summary>
    internal static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    internal sealed record Watch(string Project, DateTime DeadlineUtc, string Revision, Dictionary<string, string> Variables);

    internal static string DirectoryFor(string workDirectory) => Path.Combine(workDirectory, "bluegreen-confirm");

    private static string PathFor(string workDirectory, string project) => Path.Combine(DirectoryFor(workDirectory), project + ".json");

    /// <summary>The window asked for by the step, or zero when none (every environment but one).</summary>
    internal static int MinutesFrom(IReadOnlyDictionary<string, string> envVars) =>
        int.TryParse(envVars.GetValueOrDefault(MinutesVariable), out var minutes) && minutes > 0 ? minutes : 0;

    internal static void Arm(
        string workDirectory, string project, int minutes, string revision,
        IReadOnlyDictionary<string, string> envVars, DateTime nowUtc)
    {
        var variables = envVars
            .Where(entry => entry.Key.StartsWith("AETHEUS_BG_", StringComparison.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        var watch = new Watch(project, nowUtc.AddMinutes(minutes).Add(Grace), revision, variables);
        var directory = DirectoryFor(workDirectory);
        Directory.CreateDirectory(directory);
        var path = PathFor(workDirectory, project);
        var temp = $"{path}.tmp.{Environment.ProcessId}";
        File.WriteAllText(temp, JsonSerializer.Serialize(watch, Json));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Arms the window when the switch asked for one, and says so in the step log.</summary>
    internal static async Task ArmIfRequestedAsync(
        string workDirectory, string project, string revision, string? previousColour,
        IReadOnlyDictionary<string, string> envVars, DateTime nowUtc, Func<string, TaskLogLevel, Task> onOutput)
    {
        var minutes = MinutesFrom(envVars);
        if (minutes == 0) return;
        Arm(workDirectory, project, minutes, revision, envVars, nowUtc);
        await onOutput(
            $"Confirmation window armed: without a commit within {minutes} minutes, this host puts traffic back on "
            + $"{previousColour ?? "the previous colour"} by itself.", TaskLogLevel.Warning).ConfigureAwait(false);
    }

    internal static bool Disarm(string workDirectory, string project)
    {
        var path = PathFor(workDirectory, project);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    /// <summary>Every armed watch; an unreadable file is reported as null so it can be named, not skipped.</summary>
    internal static IEnumerable<(string Path, Watch? Watch)> ReadAll(string workDirectory)
    {
        var directory = DirectoryFor(workDirectory);
        if (!Directory.Exists(directory)) yield break;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            Watch? watch;
            try
            {
                watch = JsonSerializer.Deserialize<Watch>(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                watch = null;
            }
            yield return (path, watch);
        }
    }
}
