// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Artifacts;
using Aetheus.Back.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Back.Tests.Artifacts;

public class ArtifactCleanupServiceTests
{
    private static (ArtifactCleanupService Sut, IArtifactRepository Repo, IArtifactStorageService Storage, DateTime Now)
        BuildSut()
    {
        var repo = Substitute.For<IArtifactRepository>();
        var storage = Substitute.For<IArtifactStorageService>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddScoped(_ => repo);
        services.AddScoped(_ => storage);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var sut = new ArtifactCleanupService(scopeFactory, NullLogger<ArtifactCleanupService>.Instance, clock);
        return (sut, repo, storage, clock.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task CleanupExpiredArtifacts_FileDeleteFails_SkipsDbRemoval()
    {
        var (sut, repo, storage, now) = BuildSut();
        var artifact = new PipelineArtifact { Id = 1, FilePath = "test/broken.zip" };
        repo.GetExpiredAsync(now, 100, Arg.Any<CancellationToken>())
            .Returns(new List<PipelineArtifact> { artifact });
        storage.DeleteArtifactAsync("test/broken.zip", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("disk full")));

        await sut.CleanupExpiredArtifactsAsync(TestContext.Current.CancellationToken);

        await repo.DidNotReceive().RemoveAsync(artifact, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CleanupExpiredArtifacts_DeletesFileThenRemovesFromDb()
    {
        var (sut, repo, storage, now) = BuildSut();
        var artifact = new PipelineArtifact { Id = 2, FilePath = "test/good.zip" };
        repo.GetExpiredAsync(now, 100, Arg.Any<CancellationToken>())
            .Returns(new List<PipelineArtifact> { artifact });
        storage.DeleteArtifactAsync("test/good.zip", Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await sut.CleanupExpiredArtifactsAsync(TestContext.Current.CancellationToken);

        await storage.Received(1).DeleteArtifactAsync("test/good.zip", Arg.Any<CancellationToken>());
        await repo.Received(1).RemoveAsync(artifact, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CleanupExpiredArtifacts_NoExpired_DoesNothing()
    {
        var (sut, repo, storage, now) = BuildSut();
        repo.GetExpiredAsync(now, 100, Arg.Any<CancellationToken>())
            .Returns(new List<PipelineArtifact>());

        await sut.CleanupExpiredArtifactsAsync(TestContext.Current.CancellationToken);

        await storage.DidNotReceive().DeleteArtifactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await repo.DidNotReceive().RemoveAsync(Arg.Any<PipelineArtifact>(), Arg.Any<CancellationToken>());
    }
}
