// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Aetheus.Shared.DTOs.Organizations;
using Microsoft.AspNetCore.WebUtilities;

namespace Aetheus.Front.Services.Api;

/// <summary>Projects and their releases.</summary>
public sealed class ProjectsApi(HttpClient http) : ApiClientBase(http)
{

    public async Task<PaginatedResult<ProjectServerDto>> GetProjectServersPageAsync(
        int projectId, int page = 1, int pageSize = 25, string? search = null,
        string? sortBy = null, bool sortDescending = false, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await GetJsonAsync<PaginatedResult<ProjectServerDto>>(
            QueryHelpers.AddQueryString($"api/projects/{projectId}/servers", query), ct).ConfigureAwait(false) ?? new();
    }


    public async Task<List<ProjectServerDto>> GetProjectServersAsync(
        int projectId, CancellationToken ct = default)
    {
        const int pageSize = 100;
        var items = new List<ProjectServerDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetProjectServersPageAsync(
                projectId, page, pageSize, ct: ct).ConfigureAwait(false);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }


    public Task<ProjectServerDto?> CreateProjectServerAsync(int projectId, CreateProjectServerRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateProjectServerRequest, ProjectServerDto>($"api/projects/{projectId}/servers", request, ct);


    public Task<ProjectServerDto?> UpdateProjectServerAsync(int projectId, int projectServerId, UpdateProjectServerRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateProjectServerRequest, ProjectServerDto>($"api/projects/{projectId}/servers/{projectServerId}", request, ct);


    public Task<ApiStatus> DeleteProjectServerAsync(int projectId, int projectServerId, CancellationToken ct = default)
        => DeleteAsync($"api/projects/{projectId}/servers/{projectServerId}", ct);


    public async Task<PaginatedResult<ServerTaskDto>> GetProjectTasksAsync(
        int projectId, int page = 1, int pageSize = 25, string? search = null,
        string? sortBy = null, bool sortDescending = false, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await GetJsonAsync<PaginatedResult<ServerTaskDto>>(
            QueryHelpers.AddQueryString($"api/projects/{projectId}/tasks", query), ct).ConfigureAwait(false) ?? new();
    }


    public async Task<PaginatedResult<TaskLogDto>> GetProjectLogsAsync(
        int projectId, int page = 1, int pageSize = 50, string? search = null,
        string? sortBy = null, bool sortDescending = false, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await GetJsonAsync<PaginatedResult<TaskLogDto>>(
            QueryHelpers.AddQueryString($"api/projects/{projectId}/logs", query), ct).ConfigureAwait(false) ?? new();
    }


    public async Task<List<ProjectActivityDto>> GetProjectActivityAsync(int projectId, int count = 20)
    {
        return await Http.GetFromJsonAsync<List<ProjectActivityDto>>($"api/projects/{projectId}/activity?count={count}", JsonOptions.Web) ?? [];
    }

    public async Task<PaginatedResult<ProjectDto>> GetProjectsAsync(
        int page = 1,
        int pageSize = 25,
        string? search = null,
        ProjectStatus? status = null,
        string? sortBy = null,
        bool sortDescending = false,
        CancellationToken ct = default)
    {
        var query = PageQuery(page, pageSize, search);
        if (status.HasValue) query["projectStatus"] = status.Value.ToString();
        if (!string.IsNullOrEmpty(sortBy)) query["sortBy"] = sortBy;
        if (sortDescending) query["sortDescending"] = bool.TrueString;
        return await Http.GetFromJsonAsync<PaginatedResult<ProjectDto>>(
            QueryHelpers.AddQueryString("api/projects", query), JsonOptions.Web, ct) ?? new();
    }


    public async Task<ProjectDetailDto?> GetProjectDetailAsync(int id, CancellationToken ct = default)
    {
        return await Http.GetFromJsonAsync<ProjectDetailDto>($"api/projects/{id}", JsonOptions.Web, ct);
    }


    public async Task<List<ProjectDto>> GetAllProjectsAsync(
        string? search = null,
        ProjectStatus? status = null,
        string? sortBy = null,
        bool sortDescending = false,
        CancellationToken ct = default)
    {
        const int pageSize = PaginationRequest.MaxPageSize;
        var items = new List<ProjectDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetProjectsAsync(
                page, pageSize, search, status, sortBy, sortDescending, ct);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }


    public Task<ProjectDto?> CreateProjectAsync(CreateProjectRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateProjectRequest, ProjectDto>("api/projects", request, ct);


    public Task<ProjectDto?> UpdateProjectAsync(int id, UpdateProjectRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateProjectRequest, ProjectDto>($"api/projects/{id}", request, ct);


    public Task<ApiStatus> DeleteProjectAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/projects/{id}", ct);

    public async Task<PaginatedResult<ReleaseDto>> GetReleasesAsync(
        int page = 1, int pageSize = 25, int? projectId = null,
        string? sortBy = null, bool sortDescending = true)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrWhiteSpace(sortBy))
        {
            query["sortBy"] = sortBy;
            query["sortDescending"] = sortDescending.ToString();
        }
        if (projectId.HasValue) query["projectId"] = projectId.Value.ToString();
        return await Http.GetFromJsonAsync<PaginatedResult<ReleaseDto>>(
            QueryHelpers.AddQueryString("api/releases", query), JsonOptions.Web) ?? new();
    }


    public async Task<ReleaseDto?> GetReleaseAsync(int id, CancellationToken ct = default)
    {
        return await Http.GetFromJsonAsync<ReleaseDto>($"api/releases/{id}", JsonOptions.Web, ct);
    }


    public async Task<List<ReleaseDto>> GetReleasesByRunAsync(int runId, CancellationToken ct = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<List<ReleaseDto>>($"api/releases/by-run/{runId}", JsonOptions.Web, ct) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            // Cross-link enrichment is best-effort: a transport error or a malformed/empty body must
            // never break the run view - fall back to no linked releases.
            return [];
        }
    }


    public async Task<List<ReleaseDto>> SyncReleasesAsync(int projectId, CancellationToken ct = default)
        => await PostNoBodyAsync<List<ReleaseDto>>($"api/releases/sync/{projectId}", ct).ConfigureAwait(false) ?? [];


    public Task<ReleaseDto?> TriggerReleaseBuildAsync(int id, TriggerReleaseBuildRequest request, CancellationToken ct = default)
        => PostJsonAsync<TriggerReleaseBuildRequest, ReleaseDto>($"api/releases/{id}/build", request, ct);


    public async Task<ReleaseRollbackPreviewDto?> GetRollbackPreviewAsync(int id, CancellationToken ct = default)
        => await Http.GetFromJsonAsync<ReleaseRollbackPreviewDto>($"api/releases/{id}/rollback-preview", JsonOptions.Web, ct);


    public Task<ReleaseRollbackDto?> RollbackReleaseAsync(int id, RollbackReleaseRequest request, CancellationToken ct = default)
        => PostJsonAsync<RollbackReleaseRequest, ReleaseRollbackDto>($"api/releases/{id}/rollback", request, ct);


    public async Task<ReleaseDto?> PromoteReleaseAsync(int id, CancellationToken ct = default)
        => await PostNoBodyAsync<ReleaseDto>($"api/releases/{id}/promote", ct);
}
