// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Analyzers.Tests;

/// <summary>
/// SEC012 reports a thread blocked inside an async function, and stays quiet on tasks already known
/// to be complete and on synchronous code, where blocking is the only option.
/// </summary>
public sealed class Sec012Tests
{
    private static Task VerifyAsync(string source) =>
        MarkupVerifier<SEC012_NoBlockingInsideAsyncAnalyzer>.VerifyAsync(source);

    [Fact]
    public async Task BlockingInsideAnAsyncFunctionIsReported()
    {
        await VerifyAsync("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            public sealed class Worker
            {
                public async Task<int> RunAsync(Task<int> pending, Task other, ValueTask<int> value)
                {
                    await Task.Yield();
                    var first = pending.{|SEC012:Result|};
                    other.{|SEC012:Wait|}();
                    var second = value.GetAwaiter().{|SEC012:GetResult|}();
                    Thread.{|SEC012:Sleep|}(10);
                    Func<Task> inner = async () => other.ConfigureAwait(false).GetAwaiter().{|SEC012:GetResult|}();
                    await inner();
                    return first + second;
                }
            }
            """);
    }

    [Fact]
    public async Task CompletedTasksAndSynchronousCodeAreNotReported()
    {
        await VerifyAsync("""
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;
            public sealed class Worker
            {
                public async Task<int> AfterAwaitAsync(Task<int> a, Task<int> b, Task<int>[] many)
                {
                    await Task.WhenAll(a, b);
                    await Task.WhenAll(many);
                    var sum = many.Sum(task => task.Result);
                    return a.Result + b.GetAwaiter().GetResult() + many[0].Result + sum;
                }

                public async Task<int> GuardedAsync(Task<int> pending)
                {
                    await Task.Yield();
                    var viaStatus = pending.Status == TaskStatus.RanToCompletion ? pending.Result : 0;
                    if (pending.IsCompletedSuccessfully) return pending.Result + viaStatus;
                    return await pending;
                }

                // Synchronous code has no await to use instead.
                public int Blocking(Task<int> pending)
                {
                    Thread.Sleep(10);
                    return pending.Result;
                }
            }
            """);
    }

    [Fact]
    public async Task KnownFalsePositive_ATaskProvenCompleteByAnEarlyReturnIsReported()
    {
        // Documented false positive: the early return proves completion, but only a guard whose true
        // branch holds the read is recognised.
        await VerifyAsync("""
            using System.Threading.Tasks;
            public sealed class Worker
            {
                public async Task<int> ReadAsync(Task<int> pending)
                {
                    await Task.Yield();
                    if (!pending.IsCompleted) return -1;
                    return pending.{|SEC012:Result|};
                }
            }
            """);
    }
}
