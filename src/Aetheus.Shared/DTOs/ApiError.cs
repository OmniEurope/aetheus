// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.DTOs;

public sealed record ApiError
{
    public string Message { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public Dictionary<string, string[]>? Errors { get; init; }
    public string? CorrelationId { get; init; }
}
