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
    string service,
    TimeSpan? naturalExitGracePeriod = null) : Stream
{
    private static readonly TimeSpan DefaultNaturalExitGracePeriod = TimeSpan.FromSeconds(2);
    private readonly Stream _inner = process.StandardOutput.BaseStream;
    private readonly Task<string> _standardError = process.StandardError.ReadToEndAsync();
    private readonly TimeSpan _naturalExitGracePeriod =
        naturalExitGracePeriod ?? DefaultNaturalExitGracePeriod;
    private int _disposeStarted;

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
        if (disposing && Interlocked.Exchange(ref _disposeStarted, 1) == 0)
        {
            // Process.Kill(entireProcessTree: true) can itself block while the OS enumerates and
            // terminates descendants. Synchronous Stream.Dispose runs on the ASP.NET response path,
            // so transfer ownership of the complete teardown to a dedicated worker. A normal pool
            // work item can be starved precisely when the server is saturated, leaving the owned git
            // process alive after the response has gone away. The worker catches every expected
            // signalling failure and always releases the process, streams and timeout source.
            _ = Task.Factory.StartNew(
                DisposeOwnedResources,
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
        base.Dispose(disposing);
    }

    private void DisposeOwnedResources()
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            LogFailureIfAvailable();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Process already gone / could not be signalled - nothing to clean up.
        }
        finally
        {
            try
            {
                _inner.Dispose();
            }
            finally
            {
                process.Dispose();
                timeoutCts.Dispose();
            }
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        try
        {
            await WaitForNaturalExitOrKillAsync().ConfigureAwait(false);
            if (process.HasExited && process.ExitCode != 0)
            {
                var error = (await _standardError.ConfigureAwait(false)).Trim();
                logger.LogWarning(
                    "git {Service} exited with code {ExitCode} after streaming: {Error}",
                    service,
                    process.ExitCode,
                    error);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Process already gone / could not be signalled - nothing to clean up.
        }
        finally
        {
            try
            {
                await _inner.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                process.Dispose();
                timeoutCts.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }

    private async Task WaitForNaturalExitOrKillAsync()
    {
        if (process.HasExited)
            return;

        using var naturalExitTimeout = new CancellationTokenSource(_naturalExitGracePeriod);
        try
        {
            await process.WaitForExitAsync(naturalExitTimeout.Token).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException) when (naturalExitTimeout.IsCancellationRequested)
        {
            // The bounded natural-exit window elapsed.
        }

        if (!process.HasExited)
            process.Kill(entireProcessTree: true);

        using var killTimeout = new CancellationTokenSource(_naturalExitGracePeriod);
        try
        {
            await process.WaitForExitAsync(killTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (killTimeout.IsCancellationRequested)
        {
            logger.LogWarning(
                "git {Service} did not exit within {GracePeriod} after process-tree termination",
                service,
                _naturalExitGracePeriod);
        }
    }

    private void LogFailureIfAvailable()
    {
        if (!process.HasExited || process.ExitCode == 0)
            return;
        var error = _standardError.IsCompletedSuccessfully
            ? _standardError.GetAwaiter().GetResult().Trim()
            : string.Empty;
        logger.LogWarning(
            "git {Service} exited with code {ExitCode} after streaming: {Error}",
            service,
            process.ExitCode,
            error);
    }
}
