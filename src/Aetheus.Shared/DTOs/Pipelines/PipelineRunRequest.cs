// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Aetheus.Shared.Validation;

namespace Aetheus.Shared.DTOs;

/// <summary>
/// Queue-time inputs for a pipeline run. <see cref="SourceBranch"/> overrides the pipeline's
/// configured branch for this run only; it never changes the saved definition.
/// </summary>
[JsonConverter(typeof(PipelineRunRequestJsonConverter))]
public sealed record PipelineRunRequest
{
    public const int MaxParameterCount = 64;

    [BoundedDictionary(MaxParameterCount, 128, 4096)]
    public Dictionary<string, string>? Parameters { get; init; }

    [StringLength(255)]
    public string? SourceBranch { get; init; }
}
