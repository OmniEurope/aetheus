// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aetheus.Back.Tests.AppMonitoring;

/// <summary>
/// S-TECH-EFPR: the upsert retries only on the specific Postgres unique-constraint violation (SQLSTATE
/// 23505) it can recover from - a concurrent insert of the same (MonitoredAppId, Fingerprint). Any other
/// failure must propagate.
/// </summary>
public sealed class AppErrorRepositoryUniqueViolationTests
{
    [Fact]
    public void IsUniqueViolation_True_ForPostgres23505()
    {
        var pg = new PostgresException("dup", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);
        var ex = new DbUpdateException("update failed", pg);

        Assert.True(AppErrorRepository.IsUniqueViolation(ex));
    }

    [Fact]
    public void IsUniqueViolation_False_ForOtherSqlState()
    {
        var pg = new PostgresException("bad", "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation);
        var ex = new DbUpdateException("update failed", pg);

        Assert.False(AppErrorRepository.IsUniqueViolation(ex));
    }

    [Fact]
    public void IsUniqueViolation_False_WhenInnerIsNotPostgres()
    {
        var ex = new DbUpdateException("update failed", new InvalidOperationException("boom"));

        Assert.False(AppErrorRepository.IsUniqueViolation(ex));
    }
}
