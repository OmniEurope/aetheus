// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Pipelines;

/// <summary>When a run started and, once it has, ended.</summary>
public sealed record PipelineRunWindow(DateTime StartedAt, DateTime? CompletedAt);
