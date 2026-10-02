// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Analyzers.Tests;

/// <summary>
/// SEC013 enforces the codebase's own convention: an intentional swallow says why. It must accept
/// every place that reason is written in this repository, and the cancellation idioms that state it
/// in code.
/// </summary>
public sealed class Sec013Tests
{
    private static Task VerifyAsync(string source) =>
        MarkupVerifier<SEC013_EmptyCatchSaysWhyAnalyzer>.VerifyAsync(source);

    [Fact]
    public async Task AnUnexplainedEmptyCatchIsReported()
    {
        await VerifyAsync("""
            using System;
            using System.IO;
            public static class Cleanup
            {
                public static void Run(Action action)
                {
                    try { action(); }
                    {|SEC013:catch|} (IOException) { }
                    try { action(); }
                    {|SEC013:catch|} { }
                    try { action(); }
                    {|SEC013:catch|} (Exception ex) when (ex is InvalidOperationException)
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task AnExplainedOrCancellationSwallowIsNotReported()
    {
        await VerifyAsync("""
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            public static class Cleanup
            {
                public static async Task RunAsync(Func<Task> action, CancellationToken ct)
                {
                    try { await action(); }
                    catch (IOException) { } // best-effort cleanup: a leftover file is harmless
                    try { await action(); }
                    catch
                    {
                        // A logging sink must never throw into the app.
                    }
                    // The terminal is not mounted when the pane is collapsed; nothing to scroll.
                    try { await action(); } catch (InvalidOperationException) { }
                    try { await action(); }
                    catch (OperationCanceledException) { }
                    try { await action(); }
                    catch (TaskCanceledException) { }
                    try { await action(); }
                    catch (ObjectDisposedException) when (ct.IsCancellationRequested) { }
                    try { await action(); }
                    catch (IOException error) { Console.Error.WriteLine(error.Message); }
                }
            }
            """);
    }

    [Fact]
    public async Task KnownFalsePositive_AReasonWrittenInTheMethodSummaryIsNotSeen()
    {
        // Documented false positive: the reason is in the XML summary, not at the catch, so the next
        // edit of the block can separate them.
        await VerifyAsync("""
            using System.IO;
            public static class Cleanup
            {
                /// <summary>Best effort: a directory that cannot be removed now is removed later.</summary>
                public static void TryDelete(string path)
                {
                    if (!Directory.Exists(path)) return;
                    try
                    {
                        Directory.Delete(path, recursive: true);
                    }
                    {|SEC013:catch|} (IOException) { }
                }
            }
            """);
    }
}
