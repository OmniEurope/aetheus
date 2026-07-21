// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Organizations;
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Components.Shared;
using Aetheus.Back.Configuration;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace Aetheus.Back.Components.Projects;

public class ProjectService(IProjectRepository repo, IAuditService audit, IEntityChangeNotifier notifier, IOrganizationService orgService, Servers.IServerLifecycleService serverService, TimeProvider timeProvider, IMemoryCache cache) : IProjectService
{
    private const string RepoUrlsCacheKey = "projects:repo-urls";
    private static readonly TimeSpan CacheDuration = BackendRuntimeDefaults.ReferenceDataCacheDuration;
    public async Task<PaginatedResult<ProjectDto>> GetProjectsAsync(ProjectPaginationRequest request, List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var (page, pageSize) = request.Normalize();
        var (items, totalCount) = await repo.GetProjectsPagedAsync(
            request.Search, request.SortBy, request.SortDescending,
            page, pageSize, request.ProjectStatus, accessibleIds, ct).ConfigureAwait(false);

        var dtos = items.Select(MapToDto).ToList();

        // Enrich each tile with its latest git activity (one batched query, not N+1).
        var gitDates = await GetLastGitUpdatesCachedAsync(
            dtos.Select(d => d.Id).ToList(), ct).ConfigureAwait(false);
        for (var i = 0; i < dtos.Count; i++)
        {
            if (gitDates.TryGetValue(dtos[i].Id, out var date) && date.HasValue)
                dtos[i] = dtos[i] with { LastGitUpdateAt = date.Value };
        }

        return new PaginatedResult<ProjectDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ProjectDetailDto?> GetProjectDetailAsync(int id, CancellationToken ct = default)
    {
        var project = await repo.GetProjectDetailAsync(id, ct).ConfigureAwait(false);

        if (project is null) return null;

        var currentSteps = await repo.GetActiveRunStepLabelsAsync(id, ct).ConfigureAwait(false);
        var counts = await repo.GetProjectSectionCountsAsync(id, ct).ConfigureAwait(false)
            ?? new ProjectSectionCounts(0, 0, 0, 0, 0);

        var gitDates = await GetLastGitUpdatesCachedAsync([id], ct).ConfigureAwait(false);

        return new ProjectDetailDto
        {
            LastGitUpdateAt = gitDates.GetValueOrDefault(id),
            ServerCount = counts.Servers,
            ReleaseCount = counts.Releases,
            VaultCount = counts.Vaults,
            LibraryCount = counts.Libraries,
            EnvironmentCount = counts.Environments,
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            RepositoryUrl = project.RepositoryUrl,
            DefaultBranch = project.DefaultBranch,
            Status = project.Status,
            Tags = TagsHelper.DeserializeTags(project.Tags),
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt,
            ArtifactRetentionDays = project.ArtifactRetentionDays,
            ArtifactLatestRetentionDays = project.ArtifactLatestRetentionDays,
            ReleaseNumberingPattern = project.ReleaseNumberingPattern,
            Pipelines = project.Pipelines.Select(pl =>
            {
                var lastRun = pl.Runs.FirstOrDefault();
                return new PipelineDto
                {
                    Id = pl.Id,
                    Name = pl.Name,
                    Description = pl.Description,
                    YamlDefinition = pl.YamlDefinition,
                    TriggerType = pl.TriggerType,
                    LastRunStatus = lastRun?.Status,
                    LastRunAt = lastRun?.StartedAt,
                    CreatedAt = pl.CreatedAt,
                    UpdatedAt = pl.UpdatedAt,
                    RecentRuns = pl.Runs.Take(10).Select(r =>
                    {
                        var firstStep = r.StepRuns.FirstOrDefault(s => !s.IsSystem && s.ServerId != null);
                        return new PipelineRunSummaryDto
                        {
                            Id = r.Id,
                            Status = r.Status,
                            StartedAt = r.StartedAt,
                            CompletedAt = r.CompletedAt,
                            ServerName = firstStep?.Server?.Name,
                            ServerOs = firstStep?.Server?.OsDescription,
                            CurrentStep = currentSteps.GetValueOrDefault(r.Id)
                        };
                    }).ToList()
                };
            }).ToList()
        };
    }

    public async Task<ProjectDto> CreateProjectAsync(CreateProjectRequest request, CancellationToken ct = default)
    {
        var orgId = request.OrganizationId
            ?? await orgService.GetDefaultOrganizationIdAsync(ct).ConfigureAwait(false)
            ?? throw new BadRequestException("No organization available; create an organization first.");
        var project = new Project
        {
            Name = request.Name,
            Description = request.Description,
            RepositoryUrl = request.RepositoryUrl,
            DefaultBranch = request.DefaultBranch,
            Tags = JsonSerializer.Serialize(request.Tags),
            ArtifactRetentionDays = request.ArtifactRetentionDays,
            ArtifactLatestRetentionDays = request.ArtifactLatestRetentionDays,
            ReleaseNumberingPattern = request.ReleaseNumberingPattern,
            OrganizationId = orgId
        };
        await repo.AddProjectAsync(project, ct).ConfigureAwait(false);
        cache.Remove(RepoUrlsCacheKey);
        await audit.LogAsync("Created", "Project", project.Id, project.Name, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.Project, project.Id, EntityChangeOps.Created, ct, organizationId: orgId).ConfigureAwait(false);
        return MapToDto(project);
    }

    public async Task<ProjectDto?> UpdateProjectAsync(int id, UpdateProjectRequest request, CancellationToken ct = default)
    {
        var project = await repo.FindProjectWithPipelinesAsync(id, ct).ConfigureAwait(false);
        if (project is null) return null;

        project.Name = request.Name;
        project.Description = request.Description;
        project.RepositoryUrl = request.RepositoryUrl;
        project.DefaultBranch = request.DefaultBranch;
        project.Status = request.Status;
        project.Tags = JsonSerializer.Serialize(request.Tags);
        project.ArtifactRetentionDays = request.ArtifactRetentionDays;
        project.ArtifactLatestRetentionDays = request.ArtifactLatestRetentionDays;
        project.ReleaseNumberingPattern = request.ReleaseNumberingPattern;
        project.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        cache.Remove(RepoUrlsCacheKey);
        await audit.LogAsync("Updated", "Project", project.Id, project.Name, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.Project, project.Id, EntityChangeOps.Updated, ct, organizationId: project.OrganizationId).ConfigureAwait(false);
        return MapToDto(project);
    }

    public async Task<bool> DeleteProjectAsync(int id, CancellationToken ct = default)
    {
        var project = await repo.FindProjectAsync(id, ct).ConfigureAwait(false);
        if (project is null) return false;

        var name = project.Name;

        // External-Git: a project's optional GitConnectionId is a Restrict/NoAction back-reference to a
        // GitConnection that the GitConnection -> Project FK cascade-deletes with the project. Deleting
        // the project while that self-reference still points at the (also-being-cascaded) connection
        // trips PostgreSQL's multiple-cascade-path check (23503). Null the back-reference and persist it
        // first so the cascade can run cleanly. No-op for internal projects (GitConnectionId already null).
        if (project.GitConnectionId is not null)
        {
            project.GitConnectionId = null;
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        await repo.RemoveProjectAsync(project, ct).ConfigureAwait(false);
        cache.Remove(RepoUrlsCacheKey);
        await audit.LogAsync("Deleted", "Project", id, name, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.Project, id, EntityChangeOps.Deleted, ct, organizationId: project.OrganizationId).ConfigureAwait(false);
        return true;
    }

    // S-TECH-G7LM: the per-project "last git update" is a relatively expensive batched lookup that
    // changes only on push/sync. Cache it per id (~10 min, like RepoUrlsCacheKey) so repeated project
    // list / detail loads don't re-query. A DateTime.MinValue sentinel encodes "never" so a null value
    // is still a positive cache hit (IMemoryCache can't round-trip a null reliably).
    private async Task<Dictionary<int, DateTime?>> GetLastGitUpdatesCachedAsync(
        IReadOnlyList<int> ids, CancellationToken ct)
    {
        var result = new Dictionary<int, DateTime?>(ids.Count);
        var misses = new List<int>();
        foreach (var id in ids)
        {
            if (cache.TryGetValue(GitUpdateCacheKey(id), out DateTime cached))
            {
                result[id] = cached == DateTime.MinValue ? null : cached;
            }
            else
            {
                misses.Add(id);
            }
        }

        if (misses.Count > 0)
        {
            var fetched = await repo.GetLastGitUpdatesAsync(misses, ct).ConfigureAwait(false);
            foreach (var id in misses)
            {
                var value = fetched.GetValueOrDefault(id);
                cache.Set(GitUpdateCacheKey(id), value ?? DateTime.MinValue, CacheDuration);
                result[id] = value;
            }
        }

        return result;
    }

    private static string GitUpdateCacheKey(int id) => Services.ProjectCacheKeys.GitUpdate(id);

    public async Task<List<ProjectDto>> GetProjectsWithRepoUrlAsync(CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync(RepoUrlsCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            var projects = await repo.GetAllProjectsWithRepoUrlAsync(ct).ConfigureAwait(false);
            return projects.Select(MapToDto).ToList();
        }).ConfigureAwait(false) ?? [];
    }

    public async Task<List<ProjectServerDto>> GetProjectServersAsync(int projectId, CancellationToken ct = default)
    {
        var items = await repo.GetProjectServersAsync(projectId, ct).ConfigureAwait(false);
        return items.Select(MapToProjectServerDto).ToList();
    }

    public async Task<PaginatedResult<ProjectServerDto>> GetProjectServersPageAsync(
        int projectId, PaginationRequest request, CancellationToken ct = default)
    {
        var (page, pageSize) = request.Normalize();
        var (items, totalCount) = await repo.GetProjectServersPageAsync(
            projectId, request.Search, request.SortBy, request.SortDescending,
            page, pageSize, ct).ConfigureAwait(false);
        return new PaginatedResult<ProjectServerDto>
        {
            Items = items.Select(MapToProjectServerDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ProjectServerDto> CreateProjectServerAsync(int projectId, CreateProjectServerRequest request, CancellationToken ct = default)
    {
        // Validate the parent project exists so a missing FK surfaces as 404 rather than a 500 DbUpdateException.
        var project = await repo.FindProjectAsync(projectId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException($"Project {projectId} not found.");

        if (request.Type == ProjectServerType.AgentServer)
        {
            if (request.ServerId is null)
                throw new BadRequestException("ServerId is required for agent server type.");

            if (!await serverService.ServerExistsAsync(request.ServerId.Value, ct).ConfigureAwait(false))
                throw new NotFoundException($"Server {request.ServerId} not found.");
        }

        var entity = new ProjectServer
        {
            ProjectId = projectId,
            ServerId = request.Type == ProjectServerType.AgentServer ? request.ServerId : null,
            Type = request.Type,
            DisplayName = request.DisplayName,
            Host = request.Host,
            Port = request.Port,
            Notes = request.Notes
        };

        await repo.AddProjectServerAsync(entity, ct).ConfigureAwait(false);
        await audit.LogAsync("Created", "ProjectServer", entity.Id, entity.DisplayName, ct).ConfigureAwait(false);
        // Carry the owning org so a non-admin org member who created the parent project in this same
        // session (not yet joined to entity-Project-{id}) still receives the sub-resource change (F-05).
        await notifier.BroadcastAsync(ResourceType.Project, projectId, EntityChangeOps.Updated, ct, organizationId: project.OrganizationId).ConfigureAwait(false);

        // Reload with navigation for DTO mapping
        var saved = await repo.GetProjectServerAsync(projectId, entity.Id, ct).ConfigureAwait(false);
        return MapToProjectServerDto(saved!);
    }

    public async Task<ProjectServerDto?> UpdateProjectServerAsync(int projectId, int projectServerId, UpdateProjectServerRequest request, CancellationToken ct = default)
    {
        var entity = await repo.GetProjectServerAsync(projectId, projectServerId, ct).ConfigureAwait(false);
        if (entity is null) return null;

        entity.DisplayName = request.DisplayName;
        entity.Host = request.Host;
        entity.Port = request.Port;
        entity.Notes = request.Notes;
        entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.LogAsync("Updated", "ProjectServer", entity.Id, entity.DisplayName, ct).ConfigureAwait(false);
        var project = await repo.FindProjectAsync(projectId, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.Project, projectId, EntityChangeOps.Updated, ct, organizationId: project?.OrganizationId).ConfigureAwait(false);
        return MapToProjectServerDto(entity);
    }

    public async Task<bool> DeleteProjectServerAsync(int projectId, int projectServerId, CancellationToken ct = default)
    {
        var entity = await repo.GetProjectServerAsync(projectId, projectServerId, ct).ConfigureAwait(false);
        if (entity is null) return false;

        var name = entity.DisplayName;
        var project = await repo.FindProjectAsync(projectId, ct).ConfigureAwait(false);
        await repo.RemoveProjectServerAsync(entity, ct).ConfigureAwait(false);
        await audit.LogAsync("Deleted", "ProjectServer", projectServerId, name, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.Project, projectId, EntityChangeOps.Updated, ct, organizationId: project?.OrganizationId).ConfigureAwait(false);
        return true;
    }

    public async Task<PaginatedResult<ServerTaskDto>> GetProjectTasksAsync(int projectId, PaginationRequest request, CancellationToken ct = default)
    {
        var (page, pageSize) = request.Normalize();
        var (items, totalCount) = await repo.GetTasksPagedAsync(
            projectId, request.Search, request.SortBy, request.SortDescending,
            page, pageSize, ct).ConfigureAwait(false);
        return new PaginatedResult<ServerTaskDto>
        {
            Items = items.Select(t => new ServerTaskDto
            {
                Id = t.Id,
                ServerId = t.ServerId,
                ServerName = t.Server?.Name ?? string.Empty,
                Name = t.Name,
                Command = t.Command,
                Executor = t.Executor,
                Status = t.Status,
                PipelineRunId = t.PipelineRunId,
                PipelineStepRunId = t.PipelineStepRunId,
                CreatedAt = t.CreatedAt,
                StartedAt = t.StartedAt,
                CompletedAt = t.CompletedAt,
                ExitCode = t.ExitCode,
                TimeoutSeconds = t.TimeoutSeconds
            }).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PaginatedResult<TaskLogDto>> GetProjectLogsAsync(int projectId, PaginationRequest request, CancellationToken ct = default)
    {
        var (page, pageSize) = request.Normalize();
        var (items, totalCount) = await repo.GetLogsPagedAsync(
            projectId, request.Search, request.SortBy, request.SortDescending,
            page, pageSize, ct).ConfigureAwait(false);
        return new PaginatedResult<TaskLogDto>
        {
            Items = items.Select(l => new TaskLogDto
            {
                Id = l.Id,
                TaskId = l.TaskId,
                Level = l.Level,
                Message = l.Message,
                Timestamp = l.Timestamp
            }).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<List<ProjectActivityDto>> GetProjectActivityAsync(int projectId, int count = 20, CancellationToken ct = default)
    {
        var tasks = await repo.GetRecentTasksAsync(projectId, count, ct).ConfigureAwait(false);
        var runs = await repo.GetRecentPipelineRunsAsync(projectId, count, ct).ConfigureAwait(false);

        var activities = new List<ProjectActivityDto>();

        foreach (var t in tasks)
        {
            activities.Add(new ProjectActivityDto
            {
                Type = "task",
                Title = $"{t.Name} on {t.Server?.Name ?? "unknown"}",
                Status = t.Status.ToString(),
                Timestamp = t.CreatedAt,
                Icon = "task_alt"
            });
        }

        foreach (var r in runs)
        {
            activities.Add(new ProjectActivityDto
            {
                Type = "pipeline",
                Title = r.Pipeline?.Name ?? $"Run #{r.Id}",
                Status = r.Status.ToString(),
                Timestamp = r.StartedAt,
                Icon = "play_circle"
            });
        }

        return activities.OrderByDescending(a => a.Timestamp).Take(count).ToList();
    }

    private static ProjectServerDto MapToProjectServerDto(ProjectServer ps) => new()
    {
        Id = ps.Id,
        ProjectId = ps.ProjectId,
        ServerId = ps.ServerId,
        Type = ps.Type,
        DisplayName = ps.DisplayName,
        Host = ps.Host,
        Port = ps.Port,
        Notes = ps.Notes,
        CreatedAt = ps.CreatedAt,
        UpdatedAt = ps.UpdatedAt,
        ServerName = ps.Server?.Name,
        ServerHostname = ps.Server?.Hostname,
        ServerStatus = ps.Server?.Status
    };

    private static ProjectDto MapToDto(Project p)
    {
        // Latest run across all of the project's pipelines (only present when the caller included
        // Runs in the query; empty otherwise - yields a null last-run, which the UI renders as "-").
        var lastRun = p.Pipelines
            .SelectMany(pl => pl.Runs)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefault();
        return new()
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            RepositoryUrl = p.RepositoryUrl,
            DefaultBranch = p.DefaultBranch,
            Status = p.Status,
            Tags = TagsHelper.DeserializeTags(p.Tags),
            PipelineCount = p.Pipelines.Count,
            OrganizationId = p.OrganizationId,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
            LastRunStatus = lastRun?.Status,
            LastRunAt = lastRun?.StartedAt
        };
    }
}
