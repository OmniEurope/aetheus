// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.DTOs;

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
