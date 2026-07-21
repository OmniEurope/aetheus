// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;

namespace Aetheus.Back.Components.Git;

/// <summary>
/// S-TECH-GP9S: a read-only <see cref="Stream"/> that streams a git subprocess' stdout straight to the
/// HTTP response (no full-packfile buffering) and owns the process + timeout token source. Used for
/// <c>upload-pack</c> (read-only clone/fetch) only; <c>receive-pack</c> stays buffered so its exit code
/// and the post-push <c>MarkPushedAsync</c> step run before the response is committed. See ADR-020 for
/// the changed error semantics (a mid-stream git failure surfaces as a truncated pkt-line stream, not a
/// 404, since the 200 is already on the wire). Disposing the stream (done by <c>FileStreamResult</c>
/// once the response is written) kills the process if still running and releases both resources.
/// </summary>
internal sealed class GitProcessOutputStream(
    Process process,
    CancellationTokenSource timeoutCts,
    ILogger logger,
    string service) : Stream
{
    private readonly Stream _inner = process.StandardOutput.BaseStream;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _inner.ReadAsync(buffer, offset, count, cancellationToken);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _inner.ReadAsync(buffer, cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                else if (process.ExitCode != 0)
                    logger.LogWarning("git {Service} exited with code {ExitCode} after streaming.", service, process.ExitCode);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Process already gone / could not be signalled - nothing to clean up.
            }
            process.Dispose();
            timeoutCts.Dispose();
        }
        base.Dispose(disposing);
    }
}
