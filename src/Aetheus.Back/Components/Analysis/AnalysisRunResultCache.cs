// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Caching.Memory;

namespace Aetheus.Back.Components.Analysis;

/// <summary>
/// Recette R-485: the computed result of a finished run, kept in memory with the stamp it was computed
/// under (<see cref="AnalysisRunResultStamp"/>). An entry is used only while the run's current stamp is
/// the same: a decision, a later scan, a report or an evaluation changes the stamp, so a stale result is
/// never served and nothing has to remember to invalidate it. That also holds across the two
/// blue-green colours, each with its own cache, since the stamp is read from the shared database.
/// Bounded in size (rows held, findings and reports) and in time, so a large run cannot pin memory.
/// </summary>
public sealed class AnalysisRunResultCache : IDisposable
{
    /// <summary>Rows (findings plus reports, plus one per entry) held at most across every entry.</summary>
    internal const long SizeLimit = 200_000;

    /// <summary>A result is dropped after this long without being read.</summary>
    internal static readonly TimeSpan SlidingExpiration = TimeSpan.FromMinutes(30);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = SizeLimit });

    private sealed record Entry(AnalysisRunResultStamp Stamp, AnalysisRunGateDto Result);

    public bool TryGet(int runId, AnalysisRunResultStamp stamp, out AnalysisRunGateDto result)
    {
        if (_cache.TryGetValue(runId, out Entry? entry) && entry is not null && entry.Stamp == stamp)
        {
            result = entry.Result;
            return true;
        }
        result = default!;
        return false;
    }

    public void Set(int runId, AnalysisRunResultStamp stamp, AnalysisRunGateDto result)
    {
        var size = 1L + result.Findings.Count + result.Reports.Count + result.Violations.Count;
        if (size > SizeLimit) return;
        _cache.Set(runId, new Entry(stamp, result), new MemoryCacheEntryOptions
        {
            Size = size,
            SlidingExpiration = SlidingExpiration
        });
    }

    public void Dispose() => _cache.Dispose();
}
