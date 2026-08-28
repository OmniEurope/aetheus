// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Globalization;

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// Health-gates a colour on its own ports before traffic is allowed to depend on it.
///
/// The budget is the step's own <c>timeout_seconds</c>, not a constant: a fixed two minutes failed an
/// application whose cold start legitimately took longer, even when the operator had configured an
/// hour for the step. Waiting is the whole purpose of this step, so the operator's budget is the one
/// that applies.
/// </summary>
internal static class BlueGreenReadiness
{
    /// <summary>Gap between probes. Short enough to be responsive, long enough not to hammer a booting app.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Probes until both the backend readiness endpoint and the frontend answer, or the budget runs
    /// out. A colour is ready only when both answer: a backend that is up behind a frontend that is
    /// not would still serve a blank page after the switch.
    ///
    /// The client is supplied rather than built here (the caller takes it from the named deployment
    /// probe factory), which is what makes this loop testable without a listening socket.
    /// </summary>
    internal static async Task<bool> WaitAsync(
        HttpClient client, BlueGreenContext context, string colour, TimeSpan budget,
        ILogger logger, Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        var (front, back) = context.PortsFor(colour);
        var elapsed = Stopwatch.StartNew();
        var attempt = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            attempt++;
            if (await ProbeAsync(client, logger, $"http://127.0.0.1:{back.ToString(CultureInfo.InvariantCulture)}/health/ready", ct).ConfigureAwait(false)
                && await ProbeAsync(client, logger, $"http://127.0.0.1:{front.ToString(CultureInfo.InvariantCulture)}/", ct).ConfigureAwait(false))
            {
                return true;
            }
            if (elapsed.Elapsed + Interval >= budget) return false;
            if (attempt % 10 == 0)
            {
                await onOutput(
                    $"Still waiting for {colour} ({elapsed.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s "
                    + $"of {budget.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s)…",
                    TaskLogLevel.Info).ConfigureAwait(false);
            }
            await Task.Delay(Interval, ct).ConfigureAwait(false);
        }
    }

    private static async Task<bool> ProbeAsync(HttpClient client, ILogger logger, string url, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            logger.LogTrace(ex, "Readiness probe not answering yet: {Url}", url);
            return false;
        }
    }
}
