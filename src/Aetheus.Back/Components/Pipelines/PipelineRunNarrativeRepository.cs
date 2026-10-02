// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Data;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// What a run says about itself in words rather than in status: the warnings it accumulated, and why
/// it is currently not advancing. Both are read straight onto the run page, and both must be written
/// through a change-tracked entity - the run detail query is <c>AsNoTracking</c>, and mutating its
/// result is silently dropped, which is how a failed run once reached the page with no error text.
///
/// Extracted from <see cref="PipelineCoreRepository"/> when the waiting reason pushed it past the
/// file-size budget (FileSizeAuditTests).
/// </summary>
internal sealed class PipelineRunNarrativeRepository(AppDbContext db, TimeProvider timeProvider)
{
    public async Task AppendWarningsAsync(int runId, IReadOnlyCollection<string> warnings, CancellationToken ct = default)
    {
        if (warnings.Count == 0) return;

        var run = await db.PipelineRuns.FindAsync([runId], ct).ConfigureAwait(false);
        if (run is null) return;

        List<string> existing = string.IsNullOrEmpty(run.WarningsJson)
            ? []
            : JsonSerializer.Deserialize<List<string>>(run.WarningsJson) ?? [];
        existing.AddRange(warnings);
        run.WarningsJson = JsonSerializer.Serialize(existing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Records why the last scheduling pass dispatched nothing, or clears it with <c>null</c>.
    /// An unchanged reason writes nothing: the scheduler re-passes over a waiting run continuously, and
    /// rewriting would reset the "waiting since" clock this exists to provide.</summary>
    public async Task<bool> SetWaitingReasonAsync(int runId, string? reason, CancellationToken ct = default)
    {
        var run = await db.PipelineRuns.FindAsync([runId], ct).ConfigureAwait(false);
        if (run is null) return false;
        if (string.Equals(run.WaitingReason, reason, StringComparison.Ordinal)) return false;

        run.WaitingReason = reason;
        run.WaitingSince = reason is null ? null : timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }
}
