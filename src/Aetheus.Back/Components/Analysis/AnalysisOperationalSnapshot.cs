// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Analysis;

public sealed record AnalysisOperationalSnapshot(
    IReadOnlyList<AnalysisTrackingHealthRow> TrackingProjects,
    IReadOnlyList<AnalysisServerHealthRow> Servers,
    IReadOnlyList<AnalysisStorageHealthRow> Storage);

public sealed record AnalysisTrackingHealthRow(
    int OrganizationId,
    int ProjectId,
    string SyncStatus,
    DateTime? LastSyncAt,
    string? LastError);

public sealed record AnalysisServerHealthRow(
    int OrganizationId,
    int ServerId,
    string ServerName,
    string? ScannerCapabilitiesJson);

public sealed record AnalysisStorageHealthRow(
    int OrganizationId,
    int ProjectId,
    int ReportCount,
    long ContentBytes);
