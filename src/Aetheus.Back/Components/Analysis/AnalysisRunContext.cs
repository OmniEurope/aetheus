// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Analysis;

public sealed record AnalysisRunContext(
    int OrganizationId,
    int ProjectId,
    int PipelineRunId,
    string? BranchName,
    string? CommitHash,
    string? DefaultBranch,
    string? EnvironmentName = null,
    string ProjectName = "",
    string PipelineYaml = "");
