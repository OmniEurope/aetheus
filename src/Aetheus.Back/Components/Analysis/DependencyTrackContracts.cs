// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Analysis;

public sealed record DependencyTrackVulnerability(
    string VulnerabilityId,
    string ComponentName,
    string ComponentVersion,
    string? PackageUrl,
    AnalysisSeverity Severity,
    string Status);

public sealed record DependencyTrackSubmission(string ProjectId, IReadOnlyList<DependencyTrackVulnerability> Vulnerabilities);

public interface IDependencyTrackClient
{
    Task<DependencyTrackSubmission> SubmitAndReadAsync(
        string projectName, string projectVersion, string cycloneDxJson, CancellationToken ct);
    Task<IReadOnlyList<DependencyTrackVulnerability>> GetVulnerabilitiesAsync(string externalProjectId, CancellationToken ct);
}

public interface IDependencyTrackOutbox
{
    Task EnqueueSbomAsync(int analysisReportId, CancellationToken ct);
}

public interface IDependencyTrackSubmissionProcessor
{
    Task ProcessSbomAsync(AnalysisReportDtoContext context, string content, CancellationToken ct);
}

public sealed record AnalysisReportDtoContext(
    int ReportId,
    int OrganizationId,
    int ProjectId,
    string ExternalProjectName,
    string CommitHash);
