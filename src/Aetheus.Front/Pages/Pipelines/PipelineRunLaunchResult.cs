// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Front.Pages.Pipelines;

internal sealed record PipelineRunLaunchResult(
    PipelineRunDto TriggeredRun,
    PaginatedResult<PipelineRunDto>? RunsPage,
    PipelineDto? Pipeline);
