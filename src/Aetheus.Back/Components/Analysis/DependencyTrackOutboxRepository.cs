// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Npgsql;

namespace Aetheus.Back.Components.Analysis;

internal sealed class DependencyTrackOutboxRepository(AppDbContext db)
{
    public async Task EnqueueAsync(int reportId, DateTime now, CancellationToken ct)
    {
        if (await db.DependencyTrackOutboxItems.AnyAsync(item => item.AnalysisReportId == reportId, ct)
            .ConfigureAwait(false))
            return;

        var source = await db.AnalysisReports.AsNoTracking()
            .Where(report => report.Id == reportId)
            .Select(report => new
            {
                report.Id,
                report.OrganizationId,
                report.ProjectId,
                ProjectName = report.Project.Name,
                report.CommitHash,
                report.PipelineArtifactId,
                report.ReportPath
            })
            .SingleAsync(ct)
            .ConfigureAwait(false);
        if (!source.PipelineArtifactId.HasValue || string.IsNullOrWhiteSpace(source.ReportPath))
            throw new InvalidOperationException("A Dependency-Track outbox item requires an immutable SBOM artifact.");

        db.DependencyTrackOutboxItems.Add(new DependencyTrackOutboxItem
        {
            AnalysisReportId = source.Id,
            PipelineArtifactId = source.PipelineArtifactId,
            OrganizationId = source.OrganizationId,
            ProjectId = source.ProjectId,
            ExternalProjectName = $"org-{source.OrganizationId}/{source.ProjectName}",
            ProjectVersion = source.CommitHash ?? "unknown",
            ReportEntryPath = source.ReportPath,
            NextAttemptAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
        }
    }

    public Task<List<DependencyTrackOutboxItem>> GetDueAsync(DateTime now, int limit, CancellationToken ct) =>
        db.DependencyTrackOutboxItems
            .Where(item => item.CompletedAt == null
                && item.PipelineArtifactId != null
                && item.NextAttemptAt <= now
                && (item.Status == DependencyTrackOutboxStatuses.Pending
                    || item.Status == DependencyTrackOutboxStatuses.Retrying))
            .OrderBy(item => item.NextAttemptAt)
            .ThenBy(item => item.Id)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);

    public Task<DependencyTrackOutboxPayload?> GetPayloadAsync(int itemId, CancellationToken ct) =>
        db.DependencyTrackOutboxItems.AsNoTracking()
            .Where(item => item.Id == itemId && item.PipelineArtifactId != null)
            .Select(item => new DependencyTrackOutboxPayload(
                item.AnalysisReportId,
                item.OrganizationId,
                item.ProjectId,
                item.ExternalProjectName,
                item.ProjectVersion,
                item.ReportEntryPath,
                item.AnalysisReport.PayloadHash ?? item.AnalysisReport.ContentHash,
                item.AnalysisReport.ContentSize,
                item.PipelineArtifact!.FilePath))
            .SingleOrDefaultAsync(ct);

    public async Task MarkSucceededAsync(DependencyTrackOutboxItem item, DateTime now, CancellationToken ct)
    {
        item.Status = DependencyTrackOutboxStatuses.Succeeded;
        item.AttemptCount++;
        item.LastAttemptAt = now;
        item.CompletedAt = now;
        item.LastError = null;
        item.PipelineArtifactId = null;
        item.UpdatedAt = now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task MarkFailedAsync(
        DependencyTrackOutboxItem item,
        string error,
        bool terminal,
        DateTime nextAttemptAt,
        DateTime now,
        CancellationToken ct)
    {
        item.AttemptCount++;
        item.LastAttemptAt = now;
        item.LastError = error.Length <= 2000 ? error : error[..2000];
        item.Status = terminal ? DependencyTrackOutboxStatuses.Failed : DependencyTrackOutboxStatuses.Retrying;
        item.NextAttemptAt = nextAttemptAt;
        item.CompletedAt = terminal ? now : null;
        if (terminal) item.PipelineArtifactId = null;
        item.UpdatedAt = now;

        var tracking = await db.AnalysisTrackingProjects
            .FirstOrDefaultAsync(candidate => candidate.ProjectId == item.ProjectId, ct)
            .ConfigureAwait(false)
            ?? new AnalysisTrackingProject
            {
                OrganizationId = item.OrganizationId,
                ProjectId = item.ProjectId,
                CreatedAt = now
            };
        tracking.LastSbomReportId = item.AnalysisReportId;
        tracking.ExternalProjectName = item.ExternalProjectName;
        tracking.SyncStatus = terminal ? "Failed" : "Retrying";
        tracking.LastError = item.LastError;
        tracking.LastSyncAt = now;
        tracking.UpdatedAt = now;
        if (tracking.Id == 0) db.AnalysisTrackingProjects.Add(tracking);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

internal sealed record DependencyTrackOutboxPayload(
    int ReportId,
    int OrganizationId,
    int ProjectId,
    string ExternalProjectName,
    string ProjectVersion,
    string ReportEntryPath,
    string ContentHash,
    long ContentSize,
    string ArtifactFilePath);
