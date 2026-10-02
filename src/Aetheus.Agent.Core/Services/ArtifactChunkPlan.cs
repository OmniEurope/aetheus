// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;

namespace Aetheus.Agent.Core.Services;

/// <summary>One part of a chunked artifact upload: where it starts, how long it is, what it hashes to.</summary>
public sealed record ArtifactChunk(int Index, long Offset, long Length, string Sha256);

/// <summary>
/// Splits an artifact into parts and digests them, for an upload a single request cannot carry.
///
/// The digests are computed before anything is sent so the server can refuse a part whose bytes do
/// not match what was announced, which is what makes a resend safe: a part that arrives corrupted is
/// rejected rather than assembled. The file is read twice (once to hash, once to send), which costs
/// a second pass over the page cache and buys not holding a gigabyte in memory.
/// </summary>
public static class ArtifactChunkPlan
{
    /// <summary>Above this, the artifact is uploaded in parts. Below it, a single request is simpler
    /// and cheaper, and the server accepts both.</summary>
    public const long ChunkThresholdBytes = 256L * 1024 * 1024;

    /// <summary>Part size. Well under the server's per-request ceiling, so a part is never the thing
    /// that gets refused for being too large.</summary>
    public const long ChunkSizeBytes = 256L * 1024 * 1024;

    public sealed record Plan(string Sha256, IReadOnlyList<ArtifactChunk> Chunks);

    /// <summary>Hashes the stream from its current position to its end, in one pass, producing both
    /// the whole-artifact digest and one digest per part. Leaves the position where it found it.</summary>
    public static async Task<Plan> BuildAsync(Stream content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanSeek) throw new ArgumentException("A chunk plan needs a seekable stream.", nameof(content));

        var start = content.Position;
        var total = content.Length - start;
        var chunks = new List<ArtifactChunk>();
        using var whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];

        var index = 0;
        for (var offset = 0L; offset < total; offset += ChunkSizeBytes, index++)
        {
            var length = Math.Min(ChunkSizeBytes, total - offset);
            using var part = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var remaining = length;
            while (remaining > 0)
            {
                var wanted = (int)Math.Min(buffer.Length, remaining);
                var read = await content.ReadAsync(buffer.AsMemory(0, wanted), ct).ConfigureAwait(false);
                if (read <= 0) throw new EndOfStreamException("The artifact ended before its declared length.");
                whole.AppendData(buffer, 0, read);
                part.AppendData(buffer, 0, read);
                remaining -= read;
            }
            chunks.Add(new ArtifactChunk(index, start + offset, length, Convert.ToHexStringLower(part.GetHashAndReset())));
        }

        content.Position = start;
        return new Plan(Convert.ToHexStringLower(whole.GetHashAndReset()), chunks);
    }
}

/// <summary>
/// A read-only window over a seekable stream, so one part can be sent as a request body without
/// copying it out to another file. Seeks to the part's offset on construction and reports the part's
/// length, which is what makes HttpClient send a correct Content-Length.
/// </summary>
public sealed class ArtifactChunkStream : Stream
{
    private readonly Stream _inner;
    private readonly long _offset;
    private long _position;

    public ArtifactChunkStream(Stream inner, ArtifactChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(chunk);
        _inner = inner;
        _offset = chunk.Offset;
        Length = chunk.Length;
        _inner.Position = _offset;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length { get; }

    public override long Position
    {
        get => _position;
        set
        {
            _position = value;
            _inner.Position = _offset + value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var wanted = (int)Math.Min(buffer.Length, Length - _position);
        if (wanted <= 0) return 0;
        var read = _inner.Read(buffer[..wanted]);
        _position += read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var wanted = (int)Math.Min(buffer.Length, Length - _position);
        if (wanted <= 0) return 0;
        var read = await _inner.ReadAsync(buffer[..wanted], ct).ConfigureAwait(false);
        _position += read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    // The window never owns the underlying stream: the caller sends several parts from one file.
    protected override void Dispose(bool disposing) { }
}
