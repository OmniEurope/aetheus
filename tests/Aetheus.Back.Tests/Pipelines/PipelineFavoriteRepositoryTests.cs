// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Pipelines;

public sealed class PipelineFavoriteRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PipelineFavoriteRepository _repository;

    public PipelineFavoriteRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repository = new PipelineFavoriteRepository(_db);
    }

    [Fact]
    public async Task SetFavoriteAsync_AddsAndRemovesPersonalFavorite()
    {
        var (user, pipeline) = await SeedAsync("alice", "Build");

        var added = await _repository.SetFavoriteAsync(
            user.Username, pipeline.Id, true, TestContext.Current.CancellationToken);
        var addedAgain = await _repository.SetFavoriteAsync(
            user.Username, pipeline.Id, true, TestContext.Current.CancellationToken);

        Assert.True(added!.IsFavorite);
        Assert.True(addedAgain!.IsFavorite);
        Assert.Equal(1, await _db.PipelineFavorites.CountAsync(TestContext.Current.CancellationToken));

        var removed = await _repository.SetFavoriteAsync(
            user.Username, pipeline.Id, false, TestContext.Current.CancellationToken);

        Assert.False(removed!.IsFavorite);
        Assert.Empty(_db.PipelineFavorites);
    }

    [Fact]
    public async Task GetPipelineIdsAsync_IsolatesUsersAndAccessiblePipelines()
    {
        var (alice, first) = await SeedAsync("alice", "Build");
        var (bob, second) = await SeedAsync("bob", "Deploy");
        _db.PipelineFavorites.AddRange(
            new PipelineFavorite { UserId = alice.Id, PipelineId = first.Id },
            new PipelineFavorite { UserId = alice.Id, PipelineId = second.Id },
            new PipelineFavorite { UserId = bob.Id, PipelineId = second.Id });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var ids = await _repository.GetPipelineIdsAsync(
            alice.Username, [second.Id], TestContext.Current.CancellationToken);

        Assert.Equal([second.Id], ids);
    }

    [Fact]
    public async Task SetFavoriteAsync_UnknownPipeline_ReturnsNull()
    {
        var (user, _) = await SeedAsync("alice", "Build");

        var result = await _repository.SetFavoriteAsync(
            user.Username, 999, true, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Empty(_db.PipelineFavorites);
    }

    private async Task<(User User, Pipeline Pipeline)> SeedAsync(string username, string pipelineName)
    {
        var user = new User { Username = username, PasswordHash = "hash" };
        var pipeline = new Pipeline { Name = pipelineName };
        _db.AddRange(user, pipeline);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (user, pipeline);
    }

    public void Dispose() => _db.Dispose();
}
