// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using System.Text;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Analysis;

public sealed class AnalysisContinuousTracker(
    IAnalysisRepository repository,
    IDependencyTrackClient client,
    TimeProvider timeProvider) : IDependencyTrackSubmissionProcessor
{
    public async Task ProcessSbomAsync(AnalysisReportDtoContext context, string content, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var tracking = await repository.GetTrackingProjectAsync(context.ProjectId, ct).ConfigureAwait(false)
            ?? new AnalysisTrackingProject
            {
                OrganizationId = context.OrganizationId,
                ProjectId = context.ProjectId,
                CreatedAt = now
            };
        tracking.LastSbomReportId = context.ReportId;
        tracking.ExternalProjectName = context.ExternalProjectName;
        tracking.UpdatedAt = now;
        var submission = await client.SubmitAndReadAsync(tracking.ExternalProjectName,
            context.CommitHash, content, ct).ConfigureAwait(false);
        tracking.ExternalProjectId = submission.ProjectId;
        ApplySuccessfulSnapshot(
            tracking,
            submission.Vulnerabilities,
            context.ReportId,
            isContinuous: false,
            now,
            out var observations);
        await repository.SaveTrackingSnapshotAsync(tracking, observations, ct).ConfigureAwait(false);
    }

    internal static void ApplySuccessfulSnapshot(
        AnalysisTrackingProject tracking,
        IReadOnlyList<DependencyTrackVulnerability> vulnerabilities,
        int? reportId,
        bool isContinuous,
        DateTime now,
        out List<AnalysisVulnerabilityObservation> observations)
    {
        var snapshotHash = ComputeSnapshotHash(vulnerabilities);
        observations = string.Equals(snapshotHash, tracking.LastSnapshotHash, StringComparison.Ordinal)
            ? []
            : vulnerabilities.Select(vulnerability => new AnalysisVulnerabilityObservation
            {
                OrganizationId = tracking.OrganizationId,
                ProjectId = tracking.ProjectId,
                AnalysisTrackingProject = tracking,
                AnalysisReportId = reportId,
                VulnerabilityId = Truncate(vulnerability.VulnerabilityId, 200),
                ComponentName = Truncate(vulnerability.ComponentName, 500),
                ComponentVersion = Truncate(vulnerability.ComponentVersion, 200),
                PackageUrl = TruncateNullable(vulnerability.PackageUrl, 2000),
                Severity = vulnerability.Severity,
                Status = Truncate(vulnerability.Status, 100),
                IsContinuous = isContinuous,
                ObservedAt = now
            }).ToList();
        tracking.LastSnapshotHash = snapshotHash;
        tracking.LastKnownVulnerabilityCount = vulnerabilities.Count;
        tracking.SyncStatus = "Succeeded";
        tracking.LastError = null;
        tracking.LastSyncAt = now;
        tracking.UpdatedAt = now;
    }

    private static string ComputeSnapshotHash(IReadOnlyList<DependencyTrackVulnerability> vulnerabilities)
    {
        var canonical = string.Join('\n', vulnerabilities
            .Select(item => $"{item.VulnerabilityId}|{item.PackageUrl}|{item.ComponentName}|{item.ComponentVersion}|{item.Severity}|{item.Status}")
            .Order(StringComparer.Ordinal));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    private static string? TruncateNullable(string? value, int max) => value is null ? null : Truncate(value, max);
}
