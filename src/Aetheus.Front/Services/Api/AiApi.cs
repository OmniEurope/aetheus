// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Aetheus.Shared.DTOs.Organizations;
using Microsoft.AspNetCore.WebUtilities;

namespace Aetheus.Front.Services.Api;

/// <summary>AI runners, AI tasks and their consumption accounting.</summary>
public sealed class AiApi(HttpClient http) : ApiClientBase(http)
{
    public async Task<PaginatedResult<AiRunnerProfileDto>> GetAiRunnerProfilesAsync(
        int page = 1, int pageSize = 100, string? search = null, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, "Name", false);
        return await Http.GetFromJsonAsync<PaginatedResult<AiRunnerProfileDto>>(
            QueryHelpers.AddQueryString("api/ai/profiles", query), JsonOptions.Web, ct) ?? new();
    }


    public Task<AiRunnerProfileDto?> GetAiRunnerProfileAsync(int id, CancellationToken ct = default)
        => GetJsonAsync<AiRunnerProfileDto>($"api/ai/profiles/{id}", ct);


    public async Task<List<AiRunnerProfileDto>> GetAiRunnerProfileOptionsAsync(CancellationToken ct = default)
        => await GetJsonAsync<List<AiRunnerProfileDto>>("api/ai/profile-options", ct).ConfigureAwait(false) ?? [];


    public Task<AiRunnerProfileDto?> CreateAiRunnerProfileAsync(
        CreateAiRunnerProfileRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateAiRunnerProfileRequest, AiRunnerProfileDto>("api/ai/profiles", request, ct);


    public Task<AiRunnerProfileDto?> UpdateAiRunnerProfileAsync(
        int id, UpdateAiRunnerProfileRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateAiRunnerProfileRequest, AiRunnerProfileDto>($"api/ai/profiles/{id}", request, ct);


    public Task<ApiStatus> DeleteAiRunnerProfileAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/ai/profiles/{id}", ct);


    public async Task<PaginatedResult<AiTaskDefinitionDto>> GetAiTasksAsync(
        int page = 1, int pageSize = 100, string? search = null,
        int? projectId = null, int? serverId = null, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, "Name", false);
        if (projectId.HasValue) query["projectId"] = projectId.Value.ToString();
        if (serverId.HasValue) query["serverId"] = serverId.Value.ToString();
        return await Http.GetFromJsonAsync<PaginatedResult<AiTaskDefinitionDto>>(
            QueryHelpers.AddQueryString("api/ai/tasks", query), JsonOptions.Web, ct) ?? new();
    }


    public Task<AiTaskDefinitionDto?> GetAiTaskAsync(int id, CancellationToken ct = default)
        => GetJsonAsync<AiTaskDefinitionDto>($"api/ai/tasks/{id}", ct);


    public Task<AiTaskDefinitionDto?> CreateAiTaskAsync(
        CreateAiTaskDefinitionRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateAiTaskDefinitionRequest, AiTaskDefinitionDto>("api/ai/tasks", request, ct);


    public Task<AiTaskDefinitionDto?> UpdateAiTaskAsync(
        int id, UpdateAiTaskDefinitionRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateAiTaskDefinitionRequest, AiTaskDefinitionDto>($"api/ai/tasks/{id}", request, ct);


    public Task<ApiStatus> DeleteAiTaskAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/ai/tasks/{id}", ct);


    public Task<AiRunStartDto?> RunAiTaskAsync(int id, CancellationToken ct = default)
        => PostNoBodyAsync<AiRunStartDto>($"api/ai/tasks/{id}/run", ct);


    public async Task<PaginatedResult<AiRunResultDto>> GetAiResultsAsync(
        int page = 1, int pageSize = 25, int? definitionId = null,
        int? pipelineRunId = null, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, null, "CreatedAt", true);
        if (definitionId.HasValue) query["definitionId"] = definitionId.Value.ToString();
        if (pipelineRunId.HasValue) query["pipelineRunId"] = pipelineRunId.Value.ToString();
        return await Http.GetFromJsonAsync<PaginatedResult<AiRunResultDto>>(
            QueryHelpers.AddQueryString("api/ai/results", query), JsonOptions.Web, ct) ?? new();
    }


    public Task<AiPatchApplicationDto?> ApplyAiPatchAsync(int resultId, CancellationToken ct = default)
        => PostNoBodyAsync<AiPatchApplicationDto>($"api/ai/results/{resultId}/apply", ct);


    public Task<AiConsumptionDto?> GetAiConsumptionAsync(
        int? projectId = null, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>();
        if (projectId.HasValue) query["projectId"] = projectId.Value.ToString();
        return GetJsonAsync<AiConsumptionDto>(
            QueryHelpers.AddQueryString("api/ai/consumption", query), ct);
    }
}
