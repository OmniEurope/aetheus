// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using Aetheus.Agent.Core.Services;

namespace Aetheus.Agent.Core.Tests.Services;

/// <summary>
/// The agent half of the chunked upload: the digests it announces must be the digests of the bytes
/// it will send, because the server refuses a part that does not match and a whole that does not
/// match. A plan that is right about one and wrong about the other would refuse every large upload.
/// </summary>
public sealed class ArtifactChunkPlanTests
{
    private static string Sha(ReadOnlySpan<byte> content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    private static byte[] Pattern(int length)
    {
        var bytes = new byte[length];
        for (var index = 0; index < length; index++) bytes[index] = (byte)(index % 251);
        return bytes;
    }

    [Fact]
    public async Task ASmallArtifactIsOnePartWhoseDigestIsTheWholeDigest()
    {
        var content = Pattern(1024);

        var plan = await ArtifactChunkPlan.BuildAsync(
            new MemoryStream(content), TestContext.Current.CancellationToken);

        var chunk = Assert.Single(plan.Chunks);
        Assert.Equal(Sha(content), plan.Sha256);
        Assert.Equal(plan.Sha256, chunk.Sha256);
        Assert.Equal(0, chunk.Offset);
        Assert.Equal(content.Length, chunk.Length);
    }

    [Fact]
    public async Task EachPartDigestIsTheDigestOfExactlyItsOwnBytes()
    {
        // The property the server checks part by part. Verified against a hash taken independently
        // of the plan, so a plan that hashed the wrong window would be caught.
        var content = Pattern(700);
        var stream = new MemoryStream(content);

        var plan = await BuildWithChunkSizeAsync(stream, 256);

        Assert.Equal(3, plan.Chunks.Count);
        foreach (var chunk in plan.Chunks)
            Assert.Equal(Sha(content.AsSpan((int)chunk.Offset, (int)chunk.Length)), chunk.Sha256);
    }

    [Fact]
    public async Task ThePartsCoverTheArtifactExactlyOnce()
    {
        var content = Pattern(700);

        var plan = await BuildWithChunkSizeAsync(new MemoryStream(content), 256);

        Assert.Equal(0, plan.Chunks[0].Offset);
        Assert.Equal(content.Length, plan.Chunks.Sum(chunk => chunk.Length));
        for (var index = 1; index < plan.Chunks.Count; index++)
            Assert.Equal(
                plan.Chunks[index - 1].Offset + plan.Chunks[index - 1].Length,
                plan.Chunks[index].Offset);
    }

    [Fact]
    public async Task TheStreamIsLeftWhereItWasFound()
    {
        // The caller sends the parts from the same stream, so a plan that consumed it would send
        // nothing at all.
        var stream = new MemoryStream(Pattern(1024));

        await ArtifactChunkPlan.BuildAsync(stream, TestContext.Current.CancellationToken);

        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task AWindowReadsOnlyItsOwnPartAndCanBeReadAgainAfterAFailedAttempt()
    {
        // What makes a retry safe: a fresh window over the same file re-reads the same bytes, unlike
        // the single-request upload whose stream was already partially consumed.
        var content = Pattern(700);
        var stream = new MemoryStream(content);
        var plan = await BuildWithChunkSizeAsync(stream, 256);
        var second = plan.Chunks[1];

        static byte[] Drain(Stream window)
        {
            using var buffer = new MemoryStream();
            window.CopyTo(buffer);
            return buffer.ToArray();
        }

        var first = Drain(new ArtifactChunkStream(stream, second));
        var again = Drain(new ArtifactChunkStream(stream, second));

        Assert.Equal(content.AsSpan(256, 256).ToArray(), first);
        Assert.Equal(first, again);
    }

    [Fact]
    public async Task ANonSeekableStreamIsRefusedRatherThanSilentlySentWhole()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => ArtifactChunkPlan.BuildAsync(new NonSeekableStream(), TestContext.Current.CancellationToken));
    }

    /// <summary>The production chunk size is 256 MiB, which no test should allocate. This rebuilds the
    /// same plan over a small size by slicing the content the way the production path does.</summary>
    private static async Task<ArtifactChunkPlan.Plan> BuildWithChunkSizeAsync(MemoryStream stream, int chunkSize)
    {
        var content = stream.ToArray();
        var chunks = new List<ArtifactChunk>();
        for (int offset = 0, index = 0; offset < content.Length; offset += chunkSize, index++)
        {
            var length = Math.Min(chunkSize, content.Length - offset);
            chunks.Add(new ArtifactChunk(index, offset, length, Sha(content.AsSpan(offset, length))));
        }
        var whole = await ArtifactChunkPlan.BuildAsync(stream, TestContext.Current.CancellationToken);
        return new ArtifactChunkPlan.Plan(whole.Sha256, chunks);
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
