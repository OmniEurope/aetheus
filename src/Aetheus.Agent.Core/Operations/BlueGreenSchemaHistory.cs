// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// Reads what the shared database has already applied, so the expand/contract gate knows which
/// migrations are actually pending.
///
/// The two states this separates are the whole point: an absent history table is an empty history
/// (initial installation, nothing to stay compatible with), while a history that cannot be read means
/// the gate is blind. The first is legitimate, the second has to fail the step - treating them alike
/// would wave an unreviewed bundle through against a schema the previous colour is still serving.
/// </summary>
internal static class BlueGreenSchemaHistory
{
    /// <summary>Ceiling for either query; the step's own budget covers the migration run, not a psql call.</summary>
    private const int MaxQuerySeconds = 120;

    internal static async Task<(bool Read, IReadOnlyCollection<string> Applied)> ReadAsync(
        IShellRunner shell, BlueGreenContext context, int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        var probe = await QueryAsync(
            shell, context, "SELECT to_regclass('\"__EFMigrationsHistory\"');", timeoutSeconds, ct).ConfigureAwait(false);
        if (probe.ExitCode != 0)
        {
            await onOutput($"EF migration history cannot be read: {probe.StdErr}", TaskLogLevel.Error).ConfigureAwait(false);
            return (false, []);
        }
        if (probe.StdOut.Trim().Length == 0)
        {
            await onOutput("No EF migration history table yet; treating the schema as empty.", TaskLogLevel.Info)
                .ConfigureAwait(false);
            return (true, []);
        }

        var list = await QueryAsync(
            shell, context, "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\";",
            timeoutSeconds, ct).ConfigureAwait(false);
        if (list.ExitCode != 0)
        {
            await onOutput($"EF migration history cannot be listed: {list.StdErr}", TaskLogLevel.Error).ConfigureAwait(false);
            return (false, []);
        }

        return (true, list.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList());
    }

    /// <summary>
    /// Both statements are fixed literals against the container Compose named from the same env file,
    /// so nothing user-supplied reaches the argv.
    /// </summary>
    private static Task<ShellExecResult> QueryAsync(
        IShellRunner shell, BlueGreenContext context, string sql, int timeoutSeconds, CancellationToken ct) =>
        shell.RunExecAsync(
            "docker",
            ["exec", context.DatabaseContainer, "psql", "-U", context.DatabaseUser, "-d", context.DatabaseName, "-Atc", sql],
            ct, TimeSpan.FromSeconds(Math.Min(MaxQuerySeconds, timeoutSeconds)));
}
