// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.SystemLogs;

public sealed record ClientErrorLogRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(64)]
    public string CorrelationId { get; init; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(200)]
    public string Summary { get; init; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(2000)]
    public string Message { get; init; } = string.Empty;

    [System.ComponentModel.DataAnnotations.StringLength(500)]
    public string? Path { get; init; }
}

public record SystemLogEntryDto(
    int Id,
    DateTime Timestamp,
    string Level,
    string Category,
    string Message,
    string? Exception,
    string? CorrelationId);

public record SystemLogFileDto(
    string FileName,
    long SizeBytes,
    DateTime LastModified);
