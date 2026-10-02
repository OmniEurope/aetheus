// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using Aetheus.Back.Components.Artifacts;
using Aetheus.Back.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace Aetheus.Back.Tests.Artifacts;

/// <summary>
/// The upload path for an artifact a single request cannot carry. What matters here is not that the
/// happy path works but that the refusals do: the reason this exists at all is that the previous
/// answer (four separate artifacts reassembled by `cat` in three YAML files) verified nothing.
/// </summary>
public sealed class ChunkedArtifactUploadTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"aetheus-chunked-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
    private readonly ChunkedArtifactUploadService _sut;

    public ChunkedArtifactUploadTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ArtifactStorage:BasePath"] = _root })
            .Build();
        _sut = new ChunkedArtifactUploadService(configuration, _time);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static byte[] Bytes(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    private static string Sha(params byte[][] parts)
    {
        using var sha = SHA256.Create();
        var all = parts.SelectMany(part => part).ToArray();
        return Convert.ToHexStringLower(sha.ComputeHash(all));
    }

    private Task<ChunkedUploadSession> BeginAsync(int totalParts, string digest) =>
        _sut.BeginAsync(7, "payload.tar.gz", "Build", totalParts, digest, TestContext.Current.CancellationToken);

    private Task<ChunkedPartResult> SendAsync(string uploadId, int index, byte[] content, string? digest = null) =>
        _sut.AcceptPartAsync(
            7, uploadId, index, digest ?? Sha(content), new MemoryStream(content),
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task PartsAreAssembledInOrderAndTheWholeDigestIsVerified()
    {
        var first = Bytes("hello ");
        var second = Bytes("world");
        var session = await BeginAsync(2, Sha(first, second));

        Assert.True((await SendAsync(session.UploadId, 0, first)).Accepted);
        Assert.True((await SendAsync(session.UploadId, 1, second)).Accepted);

        await using var assembled = await _sut.OpenCompletedAsync(7, session.UploadId, TestContext.Current.CancellationToken);
        using var reader = new StreamReader(assembled);
        Assert.Equal("hello world", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task APartWhoseBytesDoNotMatchItsAnnouncedDigestIsRefused()
    {
        // The whole point: a part corrupted in flight is rejected on arrival rather than assembled
        // into the artifact that gets deployed.
        var session = await BeginAsync(1, Sha(Bytes("good")));

        var result = await SendAsync(session.UploadId, 0, Bytes("corrupted"), digest: Sha(Bytes("good")));

        Assert.False(result.Accepted);
        Assert.Contains("does not match", result.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResendingAPartAfterAFailureCompletesTheUpload()
    {
        // Resumption: a network cut costs the part, not the gigabyte. The first attempt is refused,
        // the second carries the right bytes, and the session keeps everything it already had.
        var first = Bytes("aaaa");
        var second = Bytes("bbbb");
        var session = await BeginAsync(2, Sha(first, second));

        Assert.True((await SendAsync(session.UploadId, 0, first)).Accepted);
        Assert.False((await SendAsync(session.UploadId, 1, Bytes("truncated"), digest: Sha(second))).Accepted);
        var retry = await SendAsync(session.UploadId, 1, second);

        Assert.True(retry.Accepted);
        Assert.Equal(2, retry.ReceivedParts);
        await using var assembled = await _sut.OpenCompletedAsync(7, session.UploadId, TestContext.Current.CancellationToken);
        Assert.Equal(8, assembled.Length);
    }

    [Fact]
    public async Task CompletingWithAMissingPartIsRefusedAndNamesTheCount()
    {
        var session = await BeginAsync(3, Sha(Bytes("x")));
        await SendAsync(session.UploadId, 0, Bytes("x"));

        var error = await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.OpenCompletedAsync(7, session.UploadId, TestContext.Current.CancellationToken));

        Assert.Contains("2 of 3", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartsThatAreEachValidButDoNotFormTheAnnouncedArtifactAreRefused()
    {
        // Every part matched its own digest and the whole still does not: a wrong count, a wrong
        // order, or a session resumed against a different build.
        var session = await BeginAsync(1, Sha(Bytes("expected")));
        await SendAsync(session.UploadId, 0, Bytes("valid but other"));

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.OpenCompletedAsync(7, session.UploadId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnUploadBelongingToAnotherRunIsNotVisible()
    {
        // The controller proves the agent runs the run it names; this closes the rest, an agent
        // using another run's upload id to publish into that run.
        var session = await BeginAsync(1, Sha(Bytes("x")));

        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.AcceptPartAsync(
                8, session.UploadId, 0, Sha(Bytes("x")), new MemoryStream(Bytes("x")),
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("../../escape")]
    [InlineData("not-hex-at-all-not-hex-at-all-nn")]
    [InlineData("short")]
    public async Task AnUploadIdThatIsNotTheIssuedTokenIsRefused(string uploadId)
    {
        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.AcceptPartAsync(
                7, uploadId, 0, Sha(Bytes("x")), new MemoryStream(Bytes("x")),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnUnfinishedUploadIsPurgedOnceItIsOldEnough()
    {
        var session = await BeginAsync(2, Sha(Bytes("x")));
        await SendAsync(session.UploadId, 0, Bytes("x"));

        _time.Advance(TimeSpan.FromHours(23));
        Assert.Equal(0, _sut.PurgeExpired(TimeSpan.FromHours(24)));

        _time.Advance(TimeSpan.FromHours(2));
        Assert.Equal(1, _sut.PurgeExpired(TimeSpan.FromHours(24)));
        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.OpenCompletedAsync(7, session.UploadId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADeclaredPartCountOutsideTheAllowedRangeIsRefusedAtBegin()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => BeginAsync(0, Sha(Bytes("x"))));
        await Assert.ThrowsAsync<BadRequestException>(() => BeginAsync(10_001, Sha(Bytes("x"))));
    }

    [Fact]
    public async Task ADigestThatIsNotASha256IsRefusedAtBegin()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => BeginAsync(1, "not-a-digest"));
    }
}
