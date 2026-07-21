// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// Pure argv builders for DB dump/restore (PLAN-006 4.3). No sudo, no shell: the agent runs pg_dump /
/// mysqldump / pg_restore / mysql as a normal DB client via <c>ProcessStartInfo.ArgumentList</c>. The
/// password NEVER rides in argv - it is passed via <c>PGPASSWORD</c> / <c>MYSQL_PWD</c> in the (encrypted)
/// process environment, off <c>ps</c> / <c>/proc/cmdline</c>. Pure so the argv shape is unit-testable.
/// </summary>
public static class BackupCommandBuilder
{
    private static string P(int port) => port.ToString(CultureInfo.InvariantCulture);

    /// <summary>pg_dump to a custom-format archive file.</summary>
    public static IReadOnlyList<string> PgDump(string host, int port, string user, string db, string outFile)
        => ["-h", host, "-p", P(port), "-U", user, "-F", "c", "-f", outFile, db];

    /// <summary>pg_restore into a (throwaway) database. <c>--exit-on-error</c> is required for the no-fake
    /// restore-check contract: without it pg_restore continues past errors and exits 0, which would flag a
    /// partially-corrupt archive as Verified.</summary>
    public static IReadOnlyList<string> PgRestore(string host, int port, string user, string db, string file)
        => ["-h", host, "-p", P(port), "-U", user, "-d", db, "--clean", "--if-exists", "--no-owner", "--exit-on-error", file];

    /// <summary>mysqldump to a result file.</summary>
    public static IReadOnlyList<string> MysqlDump(string host, int port, string user, string db, string outFile)
        => ["-h", host, "-P", P(port), "-u", user, "--result-file=" + outFile, "--single-transaction", db];

    /// <summary>mysql client connection args for a restore (the dump is fed on stdin).</summary>
    public static IReadOnlyList<string> MysqlRestore(string host, int port, string user, string db)
        => ["-h", host, "-P", P(port), "-u", user, db];
}
