// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.IntegrationTests;

/// <summary>
/// The per-pipeline build counter behind <c>BUILD_PIPELINE_RUNNUMBER</c>.
///
/// None of this is observable on InMemory: it has no row locks, no <c>ExecuteUpdateAsync</c> and no
/// real transactions, so the unit suite exercises a separate branch entirely. The two contracts that
/// matter only exist against a relational provider - the reservation joins an ambient transaction
/// instead of opening a nested one (which throws on the same connection), and two contenders never
/// receive the same number.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PipelineBuildNumberRepositoryIntegrationTests(PostgresFixture fixture)
    : RelationalTestBase(fixture)
{
    /// <summary>
    /// The webhook path already runs inside <c>IDbTransactionScope.ExecuteInTransactionAsync</c>. A
    /// second <c>BeginTransactionAsync</c> on the same connection throws, so a reservation that did
    /// not detect the ambient transaction failed the launch outright.
    /// </summary>
    [Fact]
    public async Task ReserveNext_InsideAnAmbientTransaction_JoinsItInsteadOfOpeningANestedOne()
    {
        await ResetAndMigrateAsync();
        await using var db = NewContext();
        var pipelineId = await SeedPipelineAsync(db);

        await using var ambient = await db.Database
            .BeginTransactionAsync(TestContext.Current.CancellationToken);
        var sut = new PipelineBuildNumberRepository(db);

        var first = await sut.ReserveNextAsync(pipelineId, TestContext.Current.CancellationToken);
        var second = await sut.ReserveNextAsync(pipelineId, TestContext.Current.CancellationToken);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        // The caller still owns the transaction: nothing below committed it on their behalf.
        Assert.NotNull(db.Database.CurrentTransaction);

        await ambient.RollbackAsync(TestContext.Current.CancellationToken);
        await using var verify = NewContext();
        var persisted = await verify.Pipelines
            .Where(pipeline => pipeline.Id == pipelineId)
            .Select(pipeline => pipeline.BuildCounter)
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, persisted);
    }

    // Without the ambient transaction the reservation owns and commits its own, so the number is
    // durable the moment it is handed out.
    [Fact]
    public async Task ReserveNext_WithoutAnAmbientTransaction_CommitsItsOwn()
    {
        await ResetAndMigrateAsync();
        await using var db = NewContext();
        var pipelineId = await SeedPipelineAsync(db);

        var reserved = await new PipelineBuildNumberRepository(db)
            .ReserveNextAsync(pipelineId, TestContext.Current.CancellationToken);

        Assert.Equal(1, reserved);
        await using var verify = NewContext();
        Assert.Equal(
            1,
            await verify.Pipelines
                .Where(pipeline => pipeline.Id == pipelineId)
                .Select(pipeline => pipeline.BuildCounter)
                .SingleAsync(TestContext.Current.CancellationToken));
    }

    // Two launches of the same pipeline must never carry the same build number: the UPDATE takes the
    // row lock, so the second contender blocks on it rather than reading the same value.
    [Fact]
    public async Task ConcurrentReservations_NeverHandOutTheSameNumber()
    {
        await ResetAndMigrateAsync();
        await using var seed = NewContext();
        var pipelineId = await SeedPipelineAsync(seed);

        var reservations = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = NewContext();
            return await new PipelineBuildNumberRepository(db)
                .ReserveNextAsync(pipelineId, TestContext.Current.CancellationToken);
        }));

        Assert.Equal(Enumerable.Range(1, 8), reservations.Order());
    }

    [Fact]
    public async Task ReserveNext_ForAPipelineThatNoLongerExists_ReturnsZero()
    {
        await ResetAndMigrateAsync();
        await using var db = NewContext();

        Assert.Equal(0, await new PipelineBuildNumberRepository(db)
            .ReserveNextAsync(999_999, TestContext.Current.CancellationToken));
    }

    private static async Task<int> SeedPipelineAsync(AppDbContext db)
    {
        var pipeline = new Pipeline { Name = $"counter-{Guid.NewGuid():N}" };
        db.Pipelines.Add(pipeline);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return pipeline.Id;
    }
}
