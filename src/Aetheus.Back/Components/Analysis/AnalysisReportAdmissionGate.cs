// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Components.Analysis;

public sealed class AnalysisReportAdmissionGate
{
    private readonly SemaphoreSlim _semaphore;

    public AnalysisReportAdmissionGate(IOptions<AnalysisPlatformOptions> options)
    {
        var maximum = Math.Clamp(options.Value.MaxConcurrentReportAdmissions, 1, 8);
        _semaphore = new SemaphoreSlim(maximum, maximum);
    }

    public async Task<IAsyncDisposable> EnterAsync(CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        return new Lease(_semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class AnalysisReportAdmissionFilter(AnalysisReportAdmissionGate gate) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(
        ResourceExecutingContext context,
        ResourceExecutionDelegate next)
    {
        await using (await gate.EnterAsync(context.HttpContext.RequestAborted).ConfigureAwait(false))
            await next().ConfigureAwait(false);
    }
}
