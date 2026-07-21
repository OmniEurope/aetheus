// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring;
using Aetheus.Back.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aetheus.Back.Tests.AppMonitoring;

/// <summary>
/// S-TECH-EFPR: exercises the retry LOOP itself (not just the exception classifier) - a unique-violation
/// on the first SaveChanges must be recovered from (detach tracked changes, re-read, re-apply) so the
/// upsert still completes idempotently rather than throwing out. A real concurrent-insert reconciliation
/// needs a live Postgres (Testcontainers); this proves the recovery path with an injected 23505.
/// </summary>
public sealed class AppErrorRepositoryRetryTests
{
    // Throws a Postgres unique-violation (23505) on the first SaveChanges, then delegates to the base.
    private sealed class ThrowOnceDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public int SaveCalls { get; private set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            if (SaveCalls == 1)
                throw new DbUpdateException("duplicate key",
                    new PostgresException("dup", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation));
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task UpsertErrors_RecoversFromUniqueViolation_AndPersistsIdempotently()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ThrowOnceDbContext(options);
        var repo = new AppErrorRepository(db);

        var at = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var errors = new List<AppErrorUpsert>
        {
            new("fp-1", "System.Exception", "boom", "Frame.A", at),
            new("fp-1", "System.Exception", "boom", "Frame.A", at.AddSeconds(1)),
        };

        var groups = await repo.UpsertErrorsAsync(1, errors, ct: TestContext.Current.CancellationToken);

        // The first SaveChanges threw 23505; the loop detached, re-read, re-applied and the second succeeded.
        Assert.Equal(2, db.SaveCalls);
        Assert.Equal(1, groups); // one distinct fingerprint
        var row = await db.AppErrorEvents.AsNoTracking().SingleAsync(e => e.MonitoredAppId == 1 && e.Fingerprint == "fp-1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, row.OccurrenceCount); // both batched occurrences counted, no duplicate row
    }
}
