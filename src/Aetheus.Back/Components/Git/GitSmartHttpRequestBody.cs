// SPDX-License-Identifier: EUPL-1.2
using System.IO.Compression;
using Microsoft.AspNetCore.Http;

namespace Aetheus.Back.Components.Git;

internal static class GitSmartHttpRequestBody
{
    internal const long MaximumDecompressedBytes = 512L * 1024 * 1024;

    public static Stream Open(HttpRequest request) =>
        Open(request.Body, request.Headers.ContentEncoding.ToString(), MaximumDecompressedBytes);

    internal static Stream Open(Stream body, string? contentEncoding, long maximumDecompressedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDecompressedBytes, 1);

        if (string.IsNullOrWhiteSpace(contentEncoding)
            || string.Equals(contentEncoding.Trim(), "identity", StringComparison.OrdinalIgnoreCase))
        {
            return new BoundedReadStream(body, maximumDecompressedBytes, disposeInner: false);
        }

        if (!string.Equals(contentEncoding.Trim(), "gzip", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadHttpRequestException(
                $"Unsupported Git request content encoding '{contentEncoding}'.",
                StatusCodes.Status415UnsupportedMediaType);
        }

        var gzip = new GZipStream(body, CompressionMode.Decompress, leaveOpen: true);
        return new BoundedReadStream(gzip, maximumDecompressedBytes, disposeInner: true);
    }

    private sealed class BoundedReadStream(Stream inner, long maximumBytes, bool disposeInner) : Stream
    {
        private long _bytesRead;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            try
            {
                var read = inner.Read(buffer, offset, LimitCount(count));
                Record(read);
                return read;
            }
            catch (InvalidDataException ex)
            {
                throw InvalidCompressedBody(ex);
            }
        }

        public override int Read(Span<byte> buffer)
        {
            try
            {
                var read = inner.Read(buffer[..LimitCount(buffer.Length)]);
                Record(read);
                return read;
            }
            catch (InvalidDataException ex)
            {
                throw InvalidCompressedBody(ex);
            }
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            try
            {
                var read = await inner.ReadAsync(
                    buffer.AsMemory(offset, LimitCount(count)),
                    cancellationToken).ConfigureAwait(false);
                Record(read);
                return read;
            }
            catch (InvalidDataException ex)
            {
                throw InvalidCompressedBody(ex);
            }
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var read = await inner.ReadAsync(
                    buffer[..LimitCount(buffer.Length)],
                    cancellationToken).ConfigureAwait(false);
                Record(read);
                return read;
            }
            catch (InvalidDataException ex)
            {
                throw InvalidCompressedBody(ex);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && disposeInner)
                inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (disposeInner)
                await inner.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        private int LimitCount(int requested)
        {
            var remainingWithSentinel = maximumBytes - _bytesRead + 1;
            if (remainingWithSentinel <= 0)
                ThrowTooLarge();
            return (int)Math.Min(requested, remainingWithSentinel);
        }

        private void Record(int read)
        {
            _bytesRead += read;
            if (_bytesRead > maximumBytes)
                ThrowTooLarge();
        }

        private static void ThrowTooLarge() =>
            throw new BadHttpRequestException(
                "The decompressed Git request body exceeds the permitted size.",
                StatusCodes.Status413PayloadTooLarge);

        private static BadHttpRequestException InvalidCompressedBody(InvalidDataException inner) =>
            new(
                "The compressed Git request body is malformed.",
                StatusCodes.Status400BadRequest,
                inner);
    }
}
