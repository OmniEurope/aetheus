// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Pipelines;

internal sealed class PipelineCoverageRepository(AppDbContext db)
{
    public async Task<List<CoverageResult>> GetCoverageResultsAsync(int runId, CancellationToken ct = default)
    {
        return await db.CoverageResults
            .AsNoTracking()
            .Where(c => c.PipelineRunId == runId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task AddCoverageResultAsync(CoverageResult result, CancellationToken ct = default)
    {
        db.CoverageResults.Add(result);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // K: coverage trend for the pipeline behind <paramref name="runId"/> - the latest coverage result of
    // each of its most recent runs, returned oldest→newest for charting. Fetches ordered rows and dedupes
    // to one-per-run in memory (avoids a fragile grouped subquery while keeping a bounded result set).
    public async Task<List<CoverageTrendRow>> GetCoverageTrendAsync(int runId, int take, CancellationToken ct = default)
    {
        var pipelineId = await db.PipelineRuns.AsNoTracking()
            .Where(r => r.Id == runId)
            .Select(r => (int?)r.PipelineId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (pipelineId is null) return [];

        var rows = await db.CoverageResults.AsNoTracking()
            .Where(c => c.PipelineRun.PipelineId == pipelineId)
            .OrderByDescending(c => c.PipelineRun.StartedAt)
            .ThenByDescending(c => c.LinesValid)
            .ThenByDescending(c => c.CreatedAt)
            .Select(c => new CoverageTrendRow(c.PipelineRunId, c.PipelineRun.StartedAt, c.LineRate, c.BranchRate))
            .Take(take * 4)
            .ToListAsync(ct).ConfigureAwait(false);

        var seen = new HashSet<int>();
        var perRun = new List<CoverageTrendRow>();
        foreach (var row in rows)
        {
            if (seen.Add(row.RunId)) perRun.Add(row);
            if (perRun.Count >= take) break;
        }
        perRun.Reverse();
        return perRun;
    }

    // S-FEAT-C4R2: complexity/CRAP trend for the pipeline behind <paramref name="runId"/> - one point
    // per recent run (avg/max CC + CRAP avg), oldest→newest for charting. Mirrors GetCoverageTrendAsync:
    // fetch a bounded ordered set and collapse to one-per-run in memory.
    public async Task<List<ComplexityTrendRow>> GetComplexityTrendAsync(int runId, int take, CancellationToken ct = default)
    {
        var pipelineId = await db.PipelineRuns.AsNoTracking()
            .Where(r => r.Id == runId)
            .Select(r => (int?)r.PipelineId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (pipelineId is null) return [];

        var keys = new[] { "complexity.cyclomatic.avg", "complexity.cyclomatic.max", "complexity.crap.avg" };
        var rows = await db.RunMetrics.AsNoTracking()
            .Where(m => m.PipelineRun.PipelineId == pipelineId && keys.Contains(m.Key))
            .OrderByDescending(m => m.PipelineRun.StartedAt)
            .ThenByDescending(m => m.CreatedAt)
            .Select(m => new { m.PipelineRunId, m.PipelineRun.StartedAt, m.Key, m.Value })
            .Take(take * 12)
            .ToListAsync(ct).ConfigureAwait(false);

        var perRun = new List<ComplexityTrendRow>();
        foreach (var grp in rows.GroupBy(r => r.PipelineRunId).OrderByDescending(g => g.First().StartedAt))
        {
            double? Pick(string key) => grp.Where(x => x.Key == key).Select(x => (double?)x.Value).FirstOrDefault();
            var avg = Pick("complexity.cyclomatic.avg");
            var max = Pick("complexity.cyclomatic.max");
            if (avg is null && max is null) continue; // no complexity data for this run
            perRun.Add(new ComplexityTrendRow(grp.Key, grp.First().StartedAt, avg ?? 0, max ?? 0, Pick("complexity.crap.avg")));
            if (perRun.Count >= take) break;
        }
        perRun.Reverse();
        return perRun;
    }

    // PLAN-005 4.4: project-level coverage trend - the latest coverage result of each recent run across
    // ALL pipelines of the project, oldest->newest. Same fetch-then-dedupe shape as the per-pipeline trend.
    public async Task<List<CoverageTrendRow>> GetProjectCoverageTrendAsync(int projectId, int take, CancellationToken ct = default)
    {
        var rows = await db.CoverageResults.AsNoTracking()
            .Where(c => c.PipelineRun.Pipeline.ProjectId == projectId)
            .OrderByDescending(c => c.PipelineRun.StartedAt)
            .ThenByDescending(c => c.LinesValid)
            .ThenByDescending(c => c.CreatedAt)
            .Select(c => new CoverageTrendRow(c.PipelineRunId, c.PipelineRun.StartedAt, c.LineRate, c.BranchRate))
            .Take(take * 4)
            .ToListAsync(ct).ConfigureAwait(false);

        var seen = new HashSet<int>();
        var perRun = new List<CoverageTrendRow>();
        foreach (var row in rows)
        {
            if (seen.Add(row.RunId)) perRun.Add(row);
            if (perRun.Count >= take) break;
        }
        perRun.Reverse();
        return perRun;
    }

    // PLAN-005 4.4: project-level complexity/CRAP trend across all pipelines of the project.
    public async Task<List<ComplexityTrendRow>> GetProjectComplexityTrendAsync(int projectId, int take, CancellationToken ct = default)
    {
        var keys = new[] { "complexity.cyclomatic.avg", "complexity.cyclomatic.max", "complexity.crap.avg" };
        var rows = await db.RunMetrics.AsNoTracking()
            .Where(m => m.PipelineRun.Pipeline.ProjectId == projectId && keys.Contains(m.Key))
            .OrderByDescending(m => m.PipelineRun.StartedAt)
            .ThenByDescending(m => m.CreatedAt)
            .Select(m => new { m.PipelineRunId, m.PipelineRun.StartedAt, m.Key, m.Value })
            .Take(take * 12)
            .ToListAsync(ct).ConfigureAwait(false);

        var perRun = new List<ComplexityTrendRow>();
        foreach (var grp in rows.GroupBy(r => r.PipelineRunId).OrderByDescending(g => g.First().StartedAt))
        {
            double? Pick(string key) => grp.Where(x => x.Key == key).Select(x => (double?)x.Value).FirstOrDefault();
            var avg = Pick("complexity.cyclomatic.avg");
            var max = Pick("complexity.cyclomatic.max");
            if (avg is null && max is null) continue;
            perRun.Add(new ComplexityTrendRow(grp.Key, grp.First().StartedAt, avg ?? 0, max ?? 0, Pick("complexity.crap.avg")));
            if (perRun.Count >= take) break;
        }
        perRun.Reverse();
        return perRun;
    }

    // PLAN-005 4.4: project-level test pass/fail trend - per recent run, the counts by outcome.
    public async Task<List<TestTrendRow>> GetProjectTestTrendAsync(int projectId, int take, CancellationToken ct = default)
    {
        var recentRunIds = await db.PipelineRuns.AsNoTracking()
            .Where(r => r.Pipeline.ProjectId == projectId && r.TestResults.Any())
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new { r.Id, r.StartedAt })
            .Take(take)
            .ToListAsync(ct).ConfigureAwait(false);
        if (recentRunIds.Count == 0) return [];

        var ids = recentRunIds.Select(r => r.Id).ToList();
        var counts = await db.TestResults.AsNoTracking()
            .Where(t => ids.Contains(t.PipelineRunId))
            .GroupBy(t => new { t.PipelineRunId, t.Outcome })
            .Select(g => new { g.Key.PipelineRunId, g.Key.Outcome, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);

        var perRun = recentRunIds.Select(r =>
        {
            int C(TestOutcome o) => counts.Where(x => x.PipelineRunId == r.Id && x.Outcome == o).Sum(x => x.Count);
            return new TestTrendRow(r.Id, r.StartedAt, C(TestOutcome.Passed), C(TestOutcome.Failed) + C(TestOutcome.Error), C(TestOutcome.Skipped));
        }).ToList();
        perRun.Reverse(); // oldest -> newest for charting
        return perRun;
    }

    public async Task<List<LintResult>> GetLintResultsAsync(int runId, CancellationToken ct = default)
    {
        return await db.LintResults
            .AsNoTracking()
            .Where(l => l.PipelineRunId == runId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task AddLintResultAsync(LintResult result, CancellationToken ct = default)
    {
        db.LintResults.Add(result);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<RunMetric>> GetRunMetricsAsync(int runId, CancellationToken ct = default)
    {
        return await db.RunMetrics
            .AsNoTracking()
            .Where(m => m.PipelineRunId == runId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task AddRunMetricsAsync(IEnumerable<RunMetric> metrics, CancellationToken ct = default)
    {
        db.RunMetrics.AddRange(metrics);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> HasAnyRunningStepInRunAsync(int runId, CancellationToken ct = default)
    {
        return await db.PipelineStepRuns
            .AsNoTracking()
            .AnyAsync(s => s.PipelineRunId == runId
                && (s.Status == TaskExecutionStatus.Running || s.Status == TaskExecutionStatus.Assigned), ct)
            .ConfigureAwait(false);
    }

    // S-TECH-56: distinct stage names (= flattened job names) with an in-flight step, for MaxParallel
    // throttling of concurrent jobs within a stage group.
    public async Task<List<string>> GetActiveStageNamesAsync(int runId, CancellationToken ct = default)
    {
        return await db.PipelineStepRuns
            .AsNoTracking()
            .Where(s => s.PipelineRunId == runId
                && (s.Status == TaskExecutionStatus.Running || s.Status == TaskExecutionStatus.Assigned))
            .Select(s => s.StageName)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<Pipeline?> FindPipelineByNameAndProjectAsync(string name, int projectId, CancellationToken ct = default)
    {
        return await db.Pipelines
            .FirstOrDefaultAsync(p => p.Name == name && p.ProjectId == projectId, ct).ConfigureAwait(false);
    }
}

/// <summary>Repository projection for a single coverage-trend point (K).</summary>
public sealed record CoverageTrendRow(int RunId, DateTime Date, double LineRate, double BranchRate);

/// <summary>Repository projection for a single complexity/CRAP trend point (L · S-FEAT-C4R2).</summary>
public sealed record ComplexityTrendRow(int RunId, DateTime Date, double AvgCyclomatic, double MaxCyclomatic, double? CrapAvg);

/// <summary>Repository projection for a single test pass/fail trend point (PLAN-005 4.4).</summary>
public sealed record TestTrendRow(int RunId, DateTime Date, int Passed, int Failed, int Skipped);
