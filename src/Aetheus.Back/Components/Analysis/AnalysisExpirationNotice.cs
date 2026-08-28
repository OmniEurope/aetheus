// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Analysis;

public sealed record AnalysisExpirationNotice(
    string Kind,
    int Id,
    int OrganizationId,
    int ProjectId,
    int? FindingId,
    DateTime ExpiresAt,
    string CreatedByUsername);
