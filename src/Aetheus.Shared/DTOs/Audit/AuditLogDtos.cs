// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.DTOs;

public sealed record AuditLogDto
{
    public int Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public int? EntityId { get; init; }
    public string? Details { get; init; }
    public DateTime Timestamp { get; init; }
}

public sealed record AuditChainVerificationResult
{
    public bool IsValid { get; init; }
    public int TotalEntries { get; init; }
    public int? FirstInvalidId { get; init; }
    public string? ErrorMessage { get; init; }
}
