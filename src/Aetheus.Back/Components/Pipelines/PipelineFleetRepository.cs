// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Pipelines;

internal sealed class PipelineFleetRepository(AppDbContext db) : IPipelineFleetRepository
{
    internal const int MaxCandidateCount = 10_000;

    public async Task<List<PipelineFleetRow>> GetCandidatesAsync(
        IReadOnlyCollection<int>? organizationIds,
        IReadOnlyCollection<int>? accessiblePipelineIds,
        string? search,
        int? projectId,
        CancellationToken ct)
    {
        var pipelines = db.Pipelines.AsNoTracking();
        if (accessiblePipelineIds is not null)
            pipelines = pipelines.Where(pipeline => accessiblePipelineIds.Contains(pipeline.Id));
        if (organizationIds is not null)
            pipelines = pipelines.Where(pipeline => organizationIds.Contains(
                pipeline.Project != null
                    ? pipeline.Project.OrganizationId
                    : pipeline.Environment != null && pipeline.Environment.Project != null
                        ? pipeline.Environment.Project.OrganizationId
                        : pipeline.ProjectServer != null ? pipeline.ProjectServer.Project.OrganizationId : 0));
        if (!string.IsNullOrWhiteSpace(search))
            pipelines = pipelines.Where(pipeline => pipeline.Name.Contains(search)
                || pipeline.YamlDefinition.Contains(search)
                || pipeline.Project != null && pipeline.Project.Name.Contains(search)
                || pipeline.Environment != null && pipeline.Environment.Name.Contains(search)
                || pipeline.ProjectServer != null && pipeline.ProjectServer.DisplayName.Contains(search));
        if (projectId is { } requestedProjectId)
            pipelines = pipelines.Where(pipeline =>
                (pipeline.ProjectId ?? pipeline.Environment!.ProjectId ?? pipeline.ProjectServer!.ProjectId)
                == requestedProjectId);
        return await Project(pipelines.OrderBy(pipeline => pipeline.Name))
            .Take(MaxCandidateCount + 1)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public Task<PipelineFleetRow?> GetAsync(int pipelineId, CancellationToken ct) =>
        Project(db.Pipelines.AsNoTracking().Where(pipeline => pipeline.Id == pipelineId))
            .FirstOrDefaultAsync(ct);

    private static IQueryable<PipelineFleetRow> Project(
        IQueryable<Data.Entities.Pipeline> pipelines) =>
        pipelines.Select(pipeline => new PipelineFleetRow(
            pipeline.Id,
            pipeline.Name,
            pipeline.Description,
            pipeline.YamlDefinition,
            pipeline.SourceBranch,
            pipeline.ProjectId,
            pipeline.ProjectId ?? pipeline.Environment!.ProjectId ?? pipeline.ProjectServer!.ProjectId,
            pipeline.EnvironmentId,
            pipeline.ProjectServerId,
            pipeline.Project != null
                ? pipeline.Project.Name
                : pipeline.Environment != null
                    ? pipeline.Environment.Name
                    : pipeline.ProjectServer != null ? pipeline.ProjectServer.DisplayName : "Unowned",
            pipeline.Project != null ? "Project" : pipeline.Environment != null ? "Environment" : "ProjectServer",
            pipeline.Project != null
                ? pipeline.Project.OrganizationId
                : pipeline.Environment != null && pipeline.Environment.Project != null
                    ? pipeline.Environment.Project.OrganizationId
                    : pipeline.ProjectServer != null ? pipeline.ProjectServer.Project.OrganizationId : 0));
}
