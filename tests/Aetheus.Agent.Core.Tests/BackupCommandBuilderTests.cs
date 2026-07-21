// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Agent.Core.Tests;

public class BackupCommandBuilderTests
{
    [Fact]
    public void PgDump_BuildsArgv_WithoutPassword()
    {
        var argv = BackupCommandBuilder.PgDump("db.local", 5432, "app", "appdb", "/tmp/out.dump");

        Assert.Equal(["-h", "db.local", "-p", "5432", "-U", "app", "-F", "c", "-f", "/tmp/out.dump", "appdb"], argv);
        Assert.DoesNotContain(argv, a => a.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PgRestore_UsesCleanIfExists_IntoTargetDb()
    {
        var argv = BackupCommandBuilder.PgRestore("db.local", 5432, "app", "scratch", "/tmp/out.dump");

        Assert.Contains("-d", argv);
        Assert.Contains("scratch", argv);
        Assert.Contains("--clean", argv);
        Assert.Contains("--if-exists", argv);
        // No-fake restore-check: pg_restore must exit non-zero on any error, else a partly-corrupt
        // archive would restore "successfully" and be flagged Verified.
        Assert.Contains("--exit-on-error", argv);
    }

    [Fact]
    public void MysqlDump_WritesToResultFile_SingleTransaction()
    {
        var argv = BackupCommandBuilder.MysqlDump("db.local", 3306, "app", "appdb", "/tmp/out.sql");

        Assert.Contains("--result-file=/tmp/out.sql", argv);
        Assert.Contains("--single-transaction", argv);
        Assert.Equal("appdb", argv[^1]);
    }

    [Fact]
    public void MysqlRestore_BuildsConnectionArgs()
    {
        var argv = BackupCommandBuilder.MysqlRestore("db.local", 3306, "app", "scratch");

        Assert.Equal(["-h", "db.local", "-P", "3306", "-u", "app", "scratch"], argv);
    }
}
