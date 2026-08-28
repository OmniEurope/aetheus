// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// The web-server configuration side of a blue-green cutover: rendering the upstream file that points
/// traffic at a colour, and putting the previous one back when a reload is rejected.
///
/// Rendering this file IS the switch; the reload only applies it. That is why a surviving placeholder
/// must stop the write rather than reach the web server, and why a restore reports whether it
/// actually succeeded instead of being assumed.
/// </summary>
internal static class BlueGreenUpstream
{
    /// <summary>
    /// Renders the template for one colour, or returns null when a placeholder survives so a
    /// half-substituted configuration can never be written.
    /// </summary>
    internal static string? Render(string template, string colour, int front, int back)
    {
        var rendered = template
            .Replace("#{FRONT_PORT}#", front.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("#{BACK_PORT}#", back.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("#{COLOR}#", colour, StringComparison.Ordinal)
            // The shell renderer these steps replace spells the same placeholder #{ACTIVE_COLOR}#, and
            // the versioned upstream templates in the repository use that spelling. Accepting both is
            // what lets a project migrate its pipeline without rewriting a template the remaining
            // shell fallback still renders - and a template keeping the other spelling would simply
            // fail the guard below rather than be written half-substituted.
            .Replace("#{ACTIVE_COLOR}#", colour, StringComparison.Ordinal);
        return rendered.Contains("#{", StringComparison.Ordinal) ? null : rendered;
    }

    /// <summary>
    /// Writes through a uniquely named temporary file then renames, so a crash mid-write cannot leave
    /// the web server a truncated configuration. The name carries a per-write component because the
    /// agent runs several tasks concurrently in one process.
    /// </summary>
    internal static async Task WriteAtomicAsync(string path, string content, CancellationToken ct)
    {
        var temp = $"{path}.tmp.{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}.{Guid.NewGuid():N}";
        await File.WriteAllTextAsync(temp, content, ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Puts back the configuration snapshotted when the transaction opened, reading the snapshot from
    /// the journal itself. Both undo paths need exactly this, so it lives with the rest of the
    /// upstream handling rather than in the executor that decides which undo to run.
    /// </summary>
    internal static async Task<bool> RestoreRecordedAsync(
        IShellRunner shell, BlueGreenContext context, string confPath, string reloadCommand,
        int timeoutSeconds, CancellationToken ct)
    {
        var snapshot = Path.Combine(context.JournalDir, "upstream.before");
        var previousConf = File.Exists(snapshot) ? await File.ReadAllTextAsync(snapshot, ct).ConfigureAwait(false) : null;
        return await RestoreAsync(shell, confPath, previousConf, reloadCommand, timeoutSeconds, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Puts back exactly the configuration that was serving and reloads, reporting whether that
    /// worked. A missing snapshot means nothing was configured before, so the file is removed rather
    /// than left pointing at a colour nobody chose.
    /// </summary>
    internal static async Task<bool> RestoreAsync(
        IShellRunner shell, string confPath, string? previousConf, string reloadCommand,
        int timeoutSeconds, CancellationToken ct)
    {
        if (previousConf is not null) await WriteAtomicAsync(confPath, previousConf, ct).ConfigureAwait(false);
        else if (File.Exists(confPath)) File.Delete(confPath);

        var reload = await shell.RunExecAsync(
            "sudo", ["-n", reloadCommand], ct, TimeSpan.FromSeconds(Math.Min(120, timeoutSeconds)))
            .ConfigureAwait(false);
        return reload.ExitCode == 0;
    }
}
