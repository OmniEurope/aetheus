// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;

namespace Aetheus.Back.Components.Git;

/// <summary>
/// Hardening (#50): a read-only <see cref="Stream"/> that owns a child
/// <see cref="Process"/> so the caller's <c>using</c> block tears down the
/// process even when the consumer abandons the stream early. Used by
/// <see cref="GitLightCliService.GetBlobStreamAsync"/> to stream a blob out of
/// <c>git show</c> without leaking the child on cancellation / exception.
/// </summary>
internal sealed class ProcessOwnedStream(Process process, Stream inner) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        inner.ReadAsync(buffer, offset, count, ct);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        inner.ReadAsync(buffer, ct);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { inner.Dispose(); } catch (IOException) { /* stream already closed */ }
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* already exited */ }
            catch (System.ComponentModel.Win32Exception) { /* access denied during teardown */ }
            process.Dispose();
        }
        base.Dispose(disposing);
    }
}
