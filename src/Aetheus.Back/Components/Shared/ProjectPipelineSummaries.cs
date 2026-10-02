// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Shared;

/// <summary>
/// Recette R-480 and R-481: a project tile shows how many pipelines the project has and how its last
/// run went. The lists used to <c>Include</c> the pipelines for that, which loaded every pipeline's
/// full YAML definition to count rows. The summary is read on its own, with the three columns it
/// needs, and set on the projects as untracked, partial <see cref="Pipeline"/> instances (only the
/// members filled below), so <see cref="ProjectDtoMapper"/> reads it as before.
/// </summary>
internal static class ProjectPipelineSummaries
{
    /// <param name="withLastRun">Also reads each pipeline's latest run (status and start).</param>
    internal static async Task AttachAsync(
        AppDbContext db, IReadOnlyList<Project> projects, bool withLastRun, CancellationToken ct)
    {
        if (projects.Count == 0) return;
        var projectIds = projects.Select(project => project.Id).ToList();
        var pipelines = await db.Pipelines.AsNoTracking()
            .Where(pipeline => pipeline.ProjectId != null && projectIds.Contains(pipeline.ProjectId.Value))
            .Select(pipeline => new
            {
                pipeline.Id,
                ProjectId = pipeline.ProjectId!.Value,
                LastRun = withLastRun
                    ? pipeline.Runs.OrderByDescending(run => run.StartedAt)
                        .Select(run => new { run.Id, run.Status, run.StartedAt })
                        .FirstOrDefault()
                    : null
            })
            .ToListAsync(ct).ConfigureAwait(false);

        var byProject = pipelines.ToLookup(pipeline => pipeline.ProjectId);
        foreach (var project in projects)
            project.Pipelines = byProject[project.Id]
                .Select(pipeline => new Pipeline
                {
                    Id = pipeline.Id,
                    ProjectId = pipeline.ProjectId,
                    Runs = pipeline.LastRun is null
                        ? []
                        : [new PipelineRun
                        {
                            Id = pipeline.LastRun.Id,
                            PipelineId = pipeline.Id,
                            Status = pipeline.LastRun.Status,
                            StartedAt = pipeline.LastRun.StartedAt
                        }]
                })
                .ToList();
    }
}
