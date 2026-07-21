// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.DTOs.Organizations;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.WebUtilities;

namespace Aetheus.Front.Services;

// Single injected HTTP client fronting every backend API domain. Consolidated from 24 former
// partial files into one non-partial class (the per-domain methods are independent; a split
// would churn every front injection site). Shared transport helpers + ApiResults records sit
// alongside. See FileSizeAudit whitelist for the size justification.
public class ApiClient(HttpClient http)
{
    private List<PipelineTemplateSummaryDto>? _templateCache;

    // --- Health ---
    // D6KR: anonymous lightweight liveness probe (returns 200 once the process is up). Used by the
    // login screen to surface backend reachability before the user even attempts to sign in.
    public async Task<bool> IsBackendLiveAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await http.GetAsync("health/live", ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    // --- Auth ---
    public Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
        => PostJsonAsync<LoginRequest, LoginResponse>("api/auth/login", request, ct);

    public async Task<List<RegistrationTokenDto>> GetRegistrationTokensAsync(CancellationToken ct = default)
        => await GetJsonAsync<List<RegistrationTokenDto>>("api/auth/registration-tokens", ct).ConfigureAwait(false) ?? [];

    // RTOK: single-token read for the wizard verify-step poll - avoids re-fetching the full list every 3 s.
    public Task<RegistrationTokenDto?> GetRegistrationTokenAsync(int id, CancellationToken ct = default)
        => GetJsonAsync<RegistrationTokenDto>($"api/auth/registration-tokens/{id}", ct);

    public Task<RegistrationTokenDto?> CreateRegistrationTokenAsync(int expirationHours = 24, CancellationToken ct = default)
        => PostJsonAsync<CreateRegistrationTokenRequest, RegistrationTokenDto>(
            "api/auth/registration-tokens",
            new CreateRegistrationTokenRequest { ExpirationHours = expirationHours },
            ct);

    // --- Personal Access Tokens (PLAN-006 4.5) ---
    public async Task<PaginatedResult<PersonalAccessTokenDto>> GetPersonalAccessTokensAsync(
        int page, int pageSize, string? search = null, string? sortBy = null,
        bool sortDescending = false, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await GetJsonAsync<PaginatedResult<PersonalAccessTokenDto>>(
            QueryHelpers.AddQueryString("api/personal-access-tokens", query), ct).ConfigureAwait(false) ?? new();
    }

    public Task<CreatedPersonalAccessTokenDto?> CreatePersonalAccessTokenAsync(CreatePersonalAccessTokenRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreatePersonalAccessTokenRequest, CreatedPersonalAccessTokenDto>("api/personal-access-tokens", request, ct);

    public Task<ApiStatus> RevokePersonalAccessTokenAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/personal-access-tokens/{id}", ct);

    // --- Servers ---
    public async Task<PaginatedResult<ServerDto>> GetServersAsync(int page = 1, int pageSize = 25, string? search = null, ServerType? type = null, ServerStatus? status = null, string? sortBy = null, bool sortDescending = false)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrEmpty(search)) query["search"] = search;
        if (type.HasValue) query["type"] = type.Value.ToString();
        if (status.HasValue) query["status"] = status.Value.ToString();
        if (!string.IsNullOrEmpty(sortBy))
        {
            query["sortBy"] = sortBy;
            query["sortDescending"] = sortDescending.ToString();
        }
        return await http.GetFromJsonAsync<PaginatedResult<ServerDto>>(QueryHelpers.AddQueryString("api/servers", query), JsonOptions.Web) ?? new();
    }

    public async Task<List<string>> GetServerNamesAsync(CancellationToken ct = default)
        => await GetJsonAsync<List<string>>("api/servers/names", ct).ConfigureAwait(false) ?? [];

    public async Task<List<ServerDto>> GetAllServersAsync()
    {
        const int pageSize = 100;
        var items = new List<ServerDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetServersAsync(page, pageSize);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public Task<ServerDetailDto?> GetServerDetailAsync(int id, CancellationToken ct = default)
        => GetJsonAsync<ServerDetailDto>($"api/servers/{id}", ct);

    public Task<ServerDto?> UpdateServerAsync(int id, UpdateServerRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateServerRequest, ServerDto>($"api/servers/{id}", request, ct);

    /// <summary>Toggles the pipeline-runner capability on a server (item #11 secure-by-default).
    /// Returns the updated DTO or null on failure (404 / forbidden).</summary>
    public Task<ServerDto?> SetPipelineRunnerEnabledAsync(int id, bool enabled, CancellationToken ct = default)
        => PostJsonAsync<UpdatePipelineRunnerRequest, ServerDto>(
            $"api/servers/{id}/pipeline-runner",
            new UpdatePipelineRunnerRequest { Enabled = enabled },
            ct);

    /// <summary>Toggles the "containers only" isolation policy on a runner. Returns the updated
    /// DTO or null on failure (404 / forbidden).</summary>
    public Task<ServerDto?> SetContainerIsolationRequiredAsync(int id, bool required, CancellationToken ct = default)
        => PostJsonAsync<UpdateContainerIsolationPolicyRequest, ServerDto>(
            $"api/servers/{id}/container-isolation-policy",
            new UpdateContainerIsolationPolicyRequest { Required = required },
            ct);

    public Task<ApiStatus> DeleteServerAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/servers/{id}", ct);

    /// <summary>
    /// Probes the server's agent. Returns <c>null</c> when the request itself
    /// failed (network / forbidden / not found); a non-null result with
    /// <see cref="ContactAgentResultDto.Reachable"/> false means the probe ran
    /// but the agent is not responding.
    /// </summary>
    public Task<ContactAgentResultDto?> ContactAgentAsync(int serverId, CancellationToken ct = default)
        => PostNoBodyAsync<ContactAgentResultDto>($"api/servers/{serverId}/contact-agent", ct);

    /// <summary>
    /// One-shot "why offline?" diagnostic - combines heartbeat freshness, agent
    /// token validity and version metadata into a single ready-to-render summary.
    /// Returns <c>null</c> when the request itself failed (network / forbidden /
    /// not found); callers should fall back to the generic error message.
    /// </summary>
    public async Task<ServerDiagnosticDto?> GetServerDiagnosticAsync(int serverId, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<ServerDiagnosticDto>($"api/servers/{serverId}/diagnostic", JsonOptions.Web, ct);
        }
        catch
        {
            return null;
        }
    }


    /// <summary>Queues an agent self-update for one server. Null when the request itself failed.</summary>
    public Task<AgentUpdateResponse?> UpdateAgentAsync(int serverId, CancellationToken ct = default)
        => PostNoBodyAsync<AgentUpdateResponse>($"api/servers/{serverId}/agent/update", ct);

    /// <summary>Queues an agent self-update for every server the caller can administer.</summary>
    public Task<AgentUpdateAllResponse?> UpdateAllAgentsAsync(CancellationToken ct = default)
        => PostNoBodyAsync<AgentUpdateAllResponse>("api/servers/agent/update-all", ct);

    public async Task<PaginatedResult<ProjectDto>> GetServerProjectsPageAsync(
        int serverId, int page, int pageSize, string? search = null, string? sortBy = null, bool sortDescending = false)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await http.GetFromJsonAsync<PaginatedResult<ProjectDto>>(
            QueryHelpers.AddQueryString($"api/servers/{serverId}/projects", query), JsonOptions.Web) ?? new();
    }

    public async Task<List<ProjectDto>> GetServerProjectsAsync(int serverId)
    {
        const int pageSize = 100;
        var items = new List<ProjectDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetServerProjectsPageAsync(serverId, page, pageSize);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public async Task<List<PipelineDto>> GetServerPipelinesAsync(int serverId)
    {
        return await http.GetFromJsonAsync<List<PipelineDto>>($"api/servers/{serverId}/pipelines", JsonOptions.Web) ?? [];
    }

    public async Task<List<VariableLibraryDto>> GetServerVariableLibrariesAsync(int serverId)
    {
        return await http.GetFromJsonAsync<List<VariableLibraryDto>>($"api/servers/{serverId}/variable-libraries", JsonOptions.Web) ?? [];
    }

    public async Task<List<VaultDto>> GetServerVaultsAsync(int serverId)
    {
        return await http.GetFromJsonAsync<List<VaultDto>>($"api/servers/{serverId}/vaults", JsonOptions.Web) ?? [];
    }

    public async Task<List<ReleaseDto>> GetServerReleasesAsync(int serverId)
    {
        return await http.GetFromJsonAsync<List<ReleaseDto>>($"api/servers/{serverId}/releases", JsonOptions.Web) ?? [];
    }

    public async Task<PaginatedResult<ServerTaskDto>> GetServerTasksAsync(int serverId, int page = 1, int pageSize = 25)
    {
        return await http.GetFromJsonAsync<PaginatedResult<ServerTaskDto>>($"api/servers/{serverId}/tasks?page={page}&pageSize={pageSize}", JsonOptions.Web) ?? new();
    }

    public async Task<PaginatedResult<TaskLogDto>> GetServerLogsAsync(int serverId, int page = 1, int pageSize = 50)
    {
        return await http.GetFromJsonAsync<PaginatedResult<TaskLogDto>>($"api/servers/{serverId}/logs?page={page}&pageSize={pageSize}", JsonOptions.Web) ?? new();
    }

    public async Task<List<ServerModuleDto>> GetServerModulesAsync(int serverId)
    {
        return await http.GetFromJsonAsync<List<ServerModuleDto>>($"api/servers/{serverId}/modules", JsonOptions.Web) ?? [];
    }

    public async Task<ServerModuleDto?> CreateServerModuleAsync(int serverId, CreateServerModuleRequest request)
    {
        var response = await http.PostAsJsonAsync($"api/servers/{serverId}/modules", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<ServerModuleDto>(JsonOptions.Web)
            : null;
    }

    public async Task<ServerModuleDto?> UpdateServerModuleAsync(int serverId, int id, UpdateServerModuleRequest request)
    {
        var response = await http.PutAsJsonAsync($"api/servers/{serverId}/modules/{id}", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<ServerModuleDto>(JsonOptions.Web)
            : null;
    }

    public async Task<ApiStatus> DeleteServerModuleAsync(int serverId, int id)
    {
        var response = await http.DeleteAsync($"api/servers/{serverId}/modules/{id}");
        return ApiStatus.From(response);
    }

    public async Task<PaginatedResult<ServerAppDto>> GetServerAppsPageAsync(
        int serverId, int page = 1, int pageSize = 25, string? search = null,
        string? sortBy = null, bool sortDescending = false, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await GetJsonAsync<PaginatedResult<ServerAppDto>>(
            QueryHelpers.AddQueryString($"api/servers/{serverId}/apps", query), ct).ConfigureAwait(false) ?? new();
    }

    public async Task<List<ServerAppDto>> GetServerAppsAsync(
        int serverId, CancellationToken ct = default)
    {
        const int pageSize = 100;
        var items = new List<ServerAppDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetServerAppsPageAsync(
                serverId, page, pageSize, ct: ct).ConfigureAwait(false);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public async Task<ServerAppDto?> CreateServerAppAsync(int serverId, CreateServerAppRequest request)
    {
        var response = await http.PostAsJsonAsync($"api/servers/{serverId}/apps", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<ServerAppDto>(JsonOptions.Web)
            : null;
    }

    public async Task<ServerAppDto?> UpdateServerAppAsync(int serverId, int id, UpdateServerAppRequest request)
    {
        var response = await http.PutAsJsonAsync($"api/servers/{serverId}/apps/{id}", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<ServerAppDto>(JsonOptions.Web)
            : null;
    }

    public async Task<ApiStatus> DeleteServerAppAsync(int serverId, int id)
    {
        var response = await http.DeleteAsync($"api/servers/{serverId}/apps/{id}");
        return ApiStatus.From(response);
    }

    // --- Tasks ---
    public async Task<PaginatedResult<ServerTaskDto>> GetTasksAsync(int page = 1, int pageSize = 25, string? search = null, TaskExecutionStatus? status = null, int? serverId = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrWhiteSpace(search)) query["search"] = search;
        if (status.HasValue) query["status"] = status.Value.ToString();
        if (serverId.HasValue) query["serverId"] = serverId.Value.ToString();
        return await http.GetFromJsonAsync<PaginatedResult<ServerTaskDto>>(QueryHelpers.AddQueryString("api/tasks", query), JsonOptions.Web) ?? new();
    }

    public async Task<ServerTaskDto?> CreateTaskAsync(CreateTaskRequest request)
    {
        var response = await http.PostAsJsonAsync("api/tasks", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<ServerTaskDto>(JsonOptions.Web)
            : null;
    }

    /// <summary>
    /// Item #7: in-flight tasks the caller can see, used by the top-bar tracker on (re)connect.
    /// Already RBAC-filtered server-side.
    /// </summary>
    public async Task<List<ServerTaskDto>> GetActiveTasksAsync(CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<List<ServerTaskDto>>("api/tasks/active", JsonOptions.Web, ct) ?? [];
    }

    // --- Pipelines (see ApiClient.Pipelines.cs) ---

    // --- Projects (see ApiClient.Pipelines.cs) ---

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
        return await http.GetFromJsonAsync<List<ProjectActivityDto>>($"api/projects/{projectId}/activity?count={count}", JsonOptions.Web) ?? [];
    }

    // --- Monitoring ---
    public async Task<DashboardOverviewDto?> GetDashboardAsync()
    {
        return await http.GetFromJsonAsync<DashboardOverviewDto>("api/monitoring/dashboard", JsonOptions.Web);
    }

    public async Task<List<ServerMetricDto>> GetServerMetricsAsync(int serverId, int hours = 24, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<List<ServerMetricDto>>($"api/monitoring/servers/{serverId}/metrics?hours={hours}", JsonOptions.Web, ct) ?? [];
    }

    // --- App Monitoring (PLAN-001) ---
    public async Task<List<MonitoredAppDto>> GetMonitoredAppsAsync(int projectId, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<List<MonitoredAppDto>>($"api/appmonitoring/projects/{projectId}/apps", JsonOptions.Web, ct) ?? [];
    }

    public async Task<MonitoredAppDto?> GetMonitoredAppAsync(int id)
    {
        return await http.GetFromJsonAsync<MonitoredAppDto>($"api/appmonitoring/apps/{id}", JsonOptions.Web);
    }

    public async Task<List<AppHealthSampleDto>> GetMonitoredAppSamplesAsync(int id, int hours = 24)
    {
        return await http.GetFromJsonAsync<List<AppHealthSampleDto>>($"api/appmonitoring/apps/{id}/samples?hours={hours}", JsonOptions.Web) ?? [];
    }

    public async Task<MonitoredAppDto?> CreateMonitoredAppAsync(int projectId, CreateMonitoredAppRequest request)
    {
        var response = await http.PostAsJsonAsync($"api/appmonitoring/projects/{projectId}/apps", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<MonitoredAppDto>(JsonOptions.Web)
            : null;
    }

    public async Task<MonitoredAppDto?> UpdateMonitoredAppAsync(int id, UpdateMonitoredAppRequest request)
    {
        var response = await http.PutAsJsonAsync($"api/appmonitoring/apps/{id}", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<MonitoredAppDto>(JsonOptions.Web)
            : null;
    }

    public async Task<ApiStatus> DeleteMonitoredAppAsync(int id)
    {
        var response = await http.DeleteAsync($"api/appmonitoring/apps/{id}");
        return ApiStatus.From(response);
    }

    public async Task<AppMonitoringSummaryDto?> GetAppMonitoringSummaryAsync(CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<AppMonitoringSummaryDto>("api/appmonitoring/summary", JsonOptions.Web, ct);
    }

    // --- App Telemetry (PLAN-001 phases 2-4: OTLP metrics/logs/errors) ---
    public async Task<IngestKeyResponse?> GenerateIngestKeyAsync(int appId)
    {
        var response = await http.PostAsync($"api/appmonitoring/apps/{appId}/ingest-key", content: null);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<IngestKeyResponse>(JsonOptions.Web)
            : null;
    }

    public async Task<ApiStatus> RevokeIngestKeyAsync(int appId)
    {
        var response = await http.DeleteAsync($"api/appmonitoring/apps/{appId}/ingest-key");
        return ApiStatus.From(response);
    }

    public async Task<List<string>> GetAppMetricNamesAsync(int appId)
    {
        return await http.GetFromJsonAsync<List<string>>($"api/appmonitoring/apps/{appId}/metrics/names", JsonOptions.Web) ?? [];
    }

    public async Task<MetricSeriesDto?> GetAppMetricSeriesAsync(int appId, string metric, int hours = 24)
    {
        return await http.GetFromJsonAsync<MetricSeriesDto>(
            $"api/appmonitoring/apps/{appId}/metrics/series?metric={Uri.EscapeDataString(metric)}&hours={hours}", JsonOptions.Web);
    }

    public async Task<List<AppMetricThresholdDto>> GetAppThresholdsAsync(int appId)
    {
        return await http.GetFromJsonAsync<List<AppMetricThresholdDto>>($"api/appmonitoring/apps/{appId}/thresholds", JsonOptions.Web) ?? [];
    }

    public async Task<AppMetricThresholdDto?> CreateAppThresholdAsync(int appId, CreateMetricThresholdRequest request)
    {
        var response = await http.PostAsJsonAsync($"api/appmonitoring/apps/{appId}/thresholds", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<AppMetricThresholdDto>(JsonOptions.Web)
            : null;
    }

    public async Task<ApiStatus> DeleteAppThresholdAsync(int thresholdId)
    {
        var response = await http.DeleteAsync($"api/appmonitoring/thresholds/{thresholdId}");
        return ApiStatus.From(response);
    }

    public async Task<PaginatedResult<AppLogEntryDto>> GetAppLogsAsync(
        int appId, int hours = 24, int? minSeverity = null, string? search = null, int page = 1, int pageSize = 100)
    {
        var url = $"api/appmonitoring/apps/{appId}/logs?hours={hours}&page={page}&pageSize={pageSize}";
        if (minSeverity is { } ms) url += $"&minSeverity={ms}";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        return await http.GetFromJsonAsync<PaginatedResult<AppLogEntryDto>>(url, JsonOptions.Web) ?? new();
    }

    public async Task<PaginatedResult<AppErrorEventDto>> GetAppErrorsAsync(int appId, int page = 1, int pageSize = 50)
    {
        return await http.GetFromJsonAsync<PaginatedResult<AppErrorEventDto>>(
            $"api/appmonitoring/apps/{appId}/errors?page={page}&pageSize={pageSize}", JsonOptions.Web) ?? new();
    }

    // --- Logs ---
    public async Task<List<TaskLogDto>> GetTaskLogsAsync(int taskId)
    {
        return await http.GetFromJsonAsync<List<TaskLogDto>>($"api/logs/task/{taskId}", JsonOptions.Web) ?? [];
    }

    public async Task<List<TaskLogDto>> GetTaskLogsAsync(int taskId, CancellationToken ct)
    {
        return await http.GetFromJsonAsync<List<TaskLogDto>>($"api/logs/task/{taskId}", JsonOptions.Web, ct) ?? [];
    }

    public async Task<List<TaskLogDto>> GetTaskLogsUnmaskedAsync(int taskId)
    {
        return await http.GetFromJsonAsync<List<TaskLogDto>>($"api/logs/task/{taskId}/unmasked", JsonOptions.Web) ?? [];
    }

    public async Task<List<TaskLogDto>> GetTaskLogsUnmaskedAsync(int taskId, CancellationToken ct)
    {
        return await http.GetFromJsonAsync<List<TaskLogDto>>($"api/logs/task/{taskId}/unmasked", JsonOptions.Web, ct) ?? [];
    }

    // --- Audit ---
    public async Task<PaginatedResult<AuditLogDto>> GetAuditLogsAsync(int page = 1, int pageSize = 50, string? search = null, string? action = null, string? entityType = null, int? entityId = null, DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrEmpty(search)) query["search"] = search;
        if (!string.IsNullOrEmpty(action)) query["action"] = action;
        if (!string.IsNullOrEmpty(entityType)) query["entityType"] = entityType;
        if (entityId.HasValue) query["entityId"] = entityId.Value.ToString();
        if (dateFrom.HasValue) query["dateFrom"] = dateFrom.Value.ToString("O");
        if (dateTo.HasValue) query["dateTo"] = dateTo.Value.ToString("O");
        return await http.GetFromJsonAsync<PaginatedResult<AuditLogDto>>(QueryHelpers.AddQueryString("api/audit", query), JsonOptions.Web)
            ?? new PaginatedResult<AuditLogDto>();
    }

    public async Task<List<string>> GetAuditActionsAsync()
    {
        return await http.GetFromJsonAsync<List<string>>("api/audit/actions", JsonOptions.Web) ?? [];
    }

    public async Task<List<string>> GetAuditEntityTypesAsync()
    {
        return await http.GetFromJsonAsync<List<string>>("api/audit/entity-types", JsonOptions.Web) ?? [];
    }

    public async Task<AuditChainVerificationResult?> VerifyAuditChainAsync()
    {
        return await http.GetFromJsonAsync<AuditChainVerificationResult>("api/audit/verify-chain", JsonOptions.Web);
    }

    public async Task<AuditChainVerificationResult?> VerifyAuditEntryAsync(int id)
    {
        var response = await http.GetAsync($"api/audit/{id}/verify");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuditChainVerificationResult>(JsonOptions.Web);
    }

    // --- Service Connections ---
    public async Task<PaginatedResult<ServiceConnectionDto>> GetServiceConnectionsAsync(int page = 1, int pageSize = 20, string? search = null, int? projectId = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrEmpty(search)) query["search"] = search;
        if (projectId.HasValue) query["projectId"] = projectId.Value.ToString();
        return await http.GetFromJsonAsync<PaginatedResult<ServiceConnectionDto>>(
            QueryHelpers.AddQueryString("api/service-connections", query), JsonOptions.Web)
            ?? new PaginatedResult<ServiceConnectionDto>();
    }

    public async Task<ServiceConnectionDetailDto?> GetServiceConnectionAsync(int id)
    {
        var response = await http.GetAsync($"api/service-connections/{id}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ServiceConnectionDetailDto>(JsonOptions.Web);
    }

    public async Task<ServiceConnectionDto?> CreateServiceConnectionAsync(CreateServiceConnectionRequest request)
    {
        var response = await http.PostAsJsonAsync("api/service-connections", request, JsonOptions.Web);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ServiceConnectionDto>(JsonOptions.Web);
    }

    public async Task<ServiceConnectionDto?> UpdateServiceConnectionAsync(int id, UpdateServiceConnectionRequest request)
    {
        var response = await http.PutAsJsonAsync($"api/service-connections/{id}", request, JsonOptions.Web);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ServiceConnectionDto>(JsonOptions.Web);
    }

    public async Task<bool> DeleteServiceConnectionAsync(int id)
    {
        var response = await http.DeleteAsync($"api/service-connections/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<ServiceConnectionTestResultDto?> TestServiceConnectionAsync(int id)
    {
        var response = await http.PostAsync($"api/service-connections/{id}/test", null);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ServiceConnectionTestResultDto>(JsonOptions.Web);
    }

    // --- Settings ---
    public async Task<List<AppSettingDto>> GetSettingsAsync()
    {
        return await http.GetFromJsonAsync<List<AppSettingDto>>("api/settings", JsonOptions.Web) ?? [];
    }

    public async Task<ApiStatus> UpdateSettingAsync(string key, string value)
    {
        var response = await http.PutAsJsonAsync($"api/settings/{Uri.EscapeDataString(key)}", new AppSettingDto { Key = key, Value = value });
        return ApiStatus.From(response);
    }

    public async Task<List<SecretDto>> GetSecretsAsync()
    {
        return await http.GetFromJsonAsync<List<SecretDto>>("api/settings/secrets", JsonOptions.Web) ?? [];
    }

    public async Task<SecretDto?> CreateSecretAsync(CreateSecretRequest request)
    {
        var response = await http.PostAsJsonAsync("api/settings/secrets", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<SecretDto>(JsonOptions.Web)
            : null;
    }

    public async Task<ApiStatus> DeleteSecretAsync(int id)
    {
        var response = await http.DeleteAsync($"api/settings/secrets/{id}");
        return ApiStatus.From(response);
    }

    // --- Docker (see ApiClient.Docker.cs) ---

    // --- Variable Libraries & Vaults (see ApiClient.Variables.cs) ---

    // --- Releases (see ApiClient.Pipelines.cs) ---

    // --- Users, Dashboards, Plugins, Alerts, Notifications (see ApiClient.Admin.cs) ---
    // --- Users ---
    public async Task<PaginatedResult<UserDto>> GetUsersAsync(int page = 1, int pageSize = 25, string? search = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrEmpty(search)) query["search"] = search;
        return await http.GetFromJsonAsync<PaginatedResult<UserDto>>(
            QueryHelpers.AddQueryString("api/users", query), JsonOptions.Web) ?? new();
    }

    public async Task<UserDto?> GetUserDetailAsync(int id, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<UserDto>($"api/users/{id}", JsonOptions.Web, ct);
    }

    public async Task<UserDto?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<UserDto>("api/users/me", JsonOptions.Web, ct);
    }

    public Task<UserDto?> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateUserRequest, UserDto>("api/users", request, ct);

    public Task<UserDto?> UpdateUserAsync(int id, UpdateUserRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateUserRequest, UserDto>($"api/users/{id}", request, ct);

    public Task<ApiStatus> DeleteUserAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/users/{id}", ct);

    public Task<ApiStatus> ChangeUserPasswordAsync(int id, ChangeUserPasswordRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<ChangeUserPasswordRequest>($"api/users/{id}/change-password", request, ct);

    public Task<ApiStatus> ChangeOwnPasswordAsync(ChangeUserPasswordRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<ChangeUserPasswordRequest>("api/users/me/change-password", request, ct);

    public async Task<List<string>> GetRolesAsync()
    {
        return await http.GetFromJsonAsync<List<string>>("api/users/roles", JsonOptions.Web) ?? [];
    }

    // --- Dashboards ---

    public async Task<List<DashboardDto>> GetDashboardsAsync()
    {
        return await http.GetFromJsonAsync<List<DashboardDto>>("api/dashboards", JsonOptions.Web) ?? [];
    }

    public async Task<DashboardDto?> GetDashboardAsync(int id, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<DashboardDto>($"api/dashboards/{id}", JsonOptions.Web, ct);
    }

    public Task<DashboardDto?> CreateDashboardAsync(CreateDashboardRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateDashboardRequest, DashboardDto>("api/dashboards", request, ct);

    public Task<DashboardDto?> UpdateDashboardAsync(int id, UpdateDashboardRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateDashboardRequest, DashboardDto>($"api/dashboards/{id}", request, ct);

    public Task<ApiStatus> DeleteDashboardAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/dashboards/{id}", ct);

    // --- Plugins ---

    public async Task<PaginatedResult<PluginRegistrationDto>> GetPluginsPageAsync(
        int page = 1, int pageSize = 25, string? search = null,
        string? sortBy = null, bool sortDescending = false, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await GetJsonAsync<PaginatedResult<PluginRegistrationDto>>(
            QueryHelpers.AddQueryString("api/plugins", query), ct).ConfigureAwait(false) ?? new();
    }

    public async Task<List<PluginRegistrationDto>> GetPluginsAsync(CancellationToken ct = default)
    {
        const int pageSize = 100;
        var items = new List<PluginRegistrationDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetPluginsPageAsync(page, pageSize, ct: ct).ConfigureAwait(false);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public async Task<PluginRegistrationDto?> GetPluginAsync(int id, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<PluginRegistrationDto>($"api/plugins/{id}", JsonOptions.Web, ct);
    }

    public Task<PluginRegistrationDto?> RegisterPluginAsync(RegisterPluginRequest request, CancellationToken ct = default)
        => PostJsonAsync<RegisterPluginRequest, PluginRegistrationDto>("api/plugins", request, ct);

    public Task<PluginRegistrationDto?> UpdatePluginAsync(int id, UpdatePluginRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdatePluginRequest, PluginRegistrationDto>($"api/plugins/{id}", request, ct);

    public Task<ApiStatus> UnregisterPluginAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/plugins/{id}", ct);

    // --- Alerts ---

    public async Task<List<AlertRuleDto>> GetAlertRulesAsync()
    {
        return await http.GetFromJsonAsync<List<AlertRuleDto>>("api/alerts", JsonOptions.Web) ?? [];
    }

    public Task<AlertRuleDto?> CreateAlertRuleAsync(CreateAlertRuleRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateAlertRuleRequest, AlertRuleDto>("api/alerts", request, ct);

    public Task<AlertRuleDto?> UpdateAlertRuleAsync(int id, UpdateAlertRuleRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateAlertRuleRequest, AlertRuleDto>($"api/alerts/{id}", request, ct);

    public Task<ApiStatus> DeleteAlertRuleAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/alerts/{id}", ct);

    // --- Notifications ---

    public async Task<PaginatedResult<NotificationChannelDto>> GetNotificationChannelsPagedAsync(
        int page = 1, int pageSize = 25, string? search = null,
        string? sortBy = null, bool sortDescending = false)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrWhiteSpace(search)) query["search"] = search;
        if (!string.IsNullOrWhiteSpace(sortBy))
        {
            query["sortBy"] = sortBy;
            query["sortDescending"] = sortDescending.ToString();
        }
        return await http.GetFromJsonAsync<PaginatedResult<NotificationChannelDto>>(
            QueryHelpers.AddQueryString("api/notifications/channels", query), JsonOptions.Web) ?? new();
    }

    public async Task<List<NotificationChannelDto>> GetNotificationChannelsAsync()
    {
        const int pageSize = 100;
        var items = new List<NotificationChannelDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetNotificationChannelsPagedAsync(page, pageSize);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public Task<NotificationChannelDto?> CreateNotificationChannelAsync(CreateNotificationChannelRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateNotificationChannelRequest, NotificationChannelDto>("api/notifications/channels", request, ct);

    public Task<ApiStatus> DeleteNotificationChannelAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/notifications/channels/{id}", ct);

    public async Task<NotificationChannelDto?> GetNotificationChannelAsync(int id)
    {
        var response = await http.GetAsync($"api/notifications/channels/{id}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NotificationChannelDto>(JsonOptions.Web);
    }

    public Task<NotificationChannelDto?> UpdateNotificationChannelAsync(int id, UpdateNotificationChannelRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateNotificationChannelRequest, NotificationChannelDto>($"api/notifications/channels/{id}", request, ct);

    public async Task<NotificationTestResultDto?> TestNotificationChannelAsync(int id)
    {
        var response = await http.PostAsync($"api/notifications/channels/{id}/test", null);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NotificationTestResultDto>(JsonOptions.Web);
    }

    public async Task<PaginatedResult<NotificationRuleDto>> GetNotificationRulesPagedAsync(
        int page = 1, int pageSize = 25, string? search = null,
        string? sortBy = null, bool sortDescending = false)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrWhiteSpace(search)) query["search"] = search;
        if (!string.IsNullOrWhiteSpace(sortBy))
        {
            query["sortBy"] = sortBy;
            query["sortDescending"] = sortDescending.ToString();
        }
        return await http.GetFromJsonAsync<PaginatedResult<NotificationRuleDto>>(
            QueryHelpers.AddQueryString("api/notifications/rules", query), JsonOptions.Web) ?? new();
    }

    public async Task<List<NotificationRuleDto>> GetNotificationRulesAsync()
    {
        const int pageSize = 100;
        var items = new List<NotificationRuleDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetNotificationRulesPagedAsync(page, pageSize);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public Task<NotificationRuleDto?> CreateNotificationRuleAsync(CreateNotificationRuleRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateNotificationRuleRequest, NotificationRuleDto>("api/notifications/rules", request, ct);

    public Task<NotificationRuleDto?> UpdateNotificationRuleAsync(int id, UpdateNotificationRuleRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateNotificationRuleRequest, NotificationRuleDto>($"api/notifications/rules/{id}", request, ct);

    public Task<ApiStatus> DeleteNotificationRuleAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/notifications/rules/{id}", ct);
    // --- Apache ---
    public async Task<ApacheDataDto> GetApacheStateAsync(int serverId, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<ApacheDataDto>($"api/servers/{serverId}/apache", JsonOptions.Web, ct) ?? new();
    }

    public async Task<List<ApacheModuleDto>> GetApacheModulesAsync(int serverId)
    {
        return await http.GetFromJsonAsync<List<ApacheModuleDto>>($"api/servers/{serverId}/apache/modules", JsonOptions.Web) ?? [];
    }

    public async Task<List<ApacheVirtualHostDto>> GetApacheVirtualHostsAsync(int serverId)
    {
        return await http.GetFromJsonAsync<List<ApacheVirtualHostDto>>($"api/servers/{serverId}/apache/vhosts", JsonOptions.Web) ?? [];
    }

    public Task<ApiStatus> ExecuteApacheActionAsync(int serverId, ApacheActionRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<ApacheActionRequest>($"api/servers/{serverId}/apache/action", request, ct);

    public Task<ApiStatus> GetApacheLogsAsync(int serverId, ApacheLogRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<ApacheLogRequest>($"api/servers/{serverId}/apache/logs", request, ct);

    public async Task<ApiStatus> GetApacheVHostConfigAsync(int serverId, string siteName, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"api/servers/{serverId}/apache/vhosts/{Uri.EscapeDataString(siteName)}/config", ct);
        return ApiStatus.From(response);
    }

    public Task<ApiStatus> SaveApacheVHostConfigAsync(int serverId, ApacheVHostSaveRequest request, CancellationToken ct = default)
        => PutJsonNoBodyAsync<ApacheVHostSaveRequest>($"api/servers/{serverId}/apache/vhosts/{Uri.EscapeDataString(request.SiteName)}/config", request, ct);

    public async Task<ApiStatus> GetApacheHtaccessAsync(int serverId, string documentRoot, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"api/servers/{serverId}/apache/htaccess?documentRoot={Uri.EscapeDataString(documentRoot)}", ct);
        return ApiStatus.From(response);
    }

    public Task<ApiStatus> SaveApacheHtaccessAsync(int serverId, ApacheHtaccessSaveRequest request, CancellationToken ct = default)
        => PutJsonNoBodyAsync<ApacheHtaccessSaveRequest>($"api/servers/{serverId}/apache/htaccess", request, ct);
    public async Task<PaginatedResult<PipelineArtifactDto>> GetProjectArtifactsAsync(
        int projectId, int page = 1, int pageSize = 25, ArtifactRetentionPolicy? policy = null, int? pipelineId = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (policy.HasValue) query["policy"] = ((int)policy.Value).ToString();
        if (pipelineId.HasValue) query["pipelineId"] = pipelineId.Value.ToString();

        var url = QueryHelpers.AddQueryString($"api/artifacts/project/{projectId}", query);
        return await http.GetFromJsonAsync<PaginatedResult<PipelineArtifactDto>>(url, JsonOptions.Web) ?? new();
    }

    public async Task<PipelineArtifactDto?> GetArtifactAsync(int id, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<PipelineArtifactDto>($"api/artifacts/{id}", JsonOptions.Web, ct);
    }

    public async Task<PipelineArtifactDto?> PromoteArtifactToEnvironmentAsync(int id, string environmentName)
    {
        var request = new PromoteArtifactRequest { EnvironmentName = environmentName };
        return await PostJsonAsync<PromoteArtifactRequest, PipelineArtifactDto>($"api/artifacts/{id}/promote", request);
    }

    public async Task<Stream?> DownloadArtifactAsync(int id)
    {
        var response = await http.GetAsync($"api/artifacts/{id}/download");
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStreamAsync();
    }
    // --- Certbot ---
    public async Task<List<CertbotCertificateDto>> GetCertbotCertificatesAsync(int serverId)
    {
        return await http.GetFromJsonAsync<List<CertbotCertificateDto>>($"api/servers/{serverId}/certbot", JsonOptions.Web) ?? [];
    }

    public Task<ApiStatus> ExecuteCertbotActionAsync(int serverId, CertbotActionRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<CertbotActionRequest>($"api/servers/{serverId}/certbot/action", request, ct);

    public Task<ApiStatus> CreateCertbotCertificateAsync(int serverId, CertbotCreateRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<CertbotCreateRequest>($"api/servers/{serverId}/certbot/create", request, ct);
    // --- Cron ---
    public Task<ApiStatus> SaveCronJobAsync(int serverId, CronJobSaveRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<CronJobSaveRequest>($"api/servers/{serverId}/cron", request, ct);

    public async Task<ApiStatus> DeleteCronJobAsync(int serverId, CronJobDeleteRequest request, CancellationToken ct = default)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Delete, $"api/servers/{serverId}/cron")
        {
            Content = JsonContent.Create(request, options: JsonOptions.Web)
        };
        var response = await http.SendAsync(msg, ct);
        return ApiStatus.From(response);
    }
    // --- Docker ---
    public async Task<List<DockerContainerDto>> GetDockerContainersAsync(int serverId, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<List<DockerContainerDto>>(
            $"api/servers/{serverId}/docker/containers", JsonOptions.Web, ct) ?? [];
    }

    public Task<ApiStatus> ExecuteDockerActionAsync(int serverId, DockerActionRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<DockerActionRequest>($"api/servers/{serverId}/docker/action", request, ct);

    public async Task<string> GetContainerLogsAsync(int serverId, DockerContainerLogsRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"api/servers/{serverId}/docker/containers/logs", request, ct);
        if (!response.IsSuccessStatusCode) return string.Empty;
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<List<DockerImageDto>> GetDockerImagesAsync(int serverId)
    {
        return await http.GetFromJsonAsync<List<DockerImageDto>>($"api/servers/{serverId}/docker/images", JsonOptions.Web) ?? [];
    }

    public Task<ApiStatus> PullDockerImageAsync(int serverId, DockerPullImageRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<DockerPullImageRequest>($"api/servers/{serverId}/docker/images/pull", request, ct);

    public Task<ApiStatus> RemoveDockerImageAsync(int serverId, string imageId, CancellationToken ct = default)
        => DeleteAsync($"api/servers/{serverId}/docker/images/{Uri.EscapeDataString(imageId)}", ct);

    public async Task<List<DockerComposeStackDto>> GetComposeStacksAsync(int serverId)
    {
        return await http.GetFromJsonAsync<List<DockerComposeStackDto>>($"api/servers/{serverId}/docker/compose", JsonOptions.Web) ?? [];
    }

    public Task<ApiStatus> ExecuteComposeActionAsync(int serverId, DockerComposeActionRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<DockerComposeActionRequest>($"api/servers/{serverId}/docker/compose/action", request, ct);

    public async Task<List<DockerNetworkDto>> GetDockerNetworksAsync(int serverId)
    {
        return await http.GetFromJsonAsync<List<DockerNetworkDto>>($"api/servers/{serverId}/docker/networks", JsonOptions.Web) ?? [];
    }

    public async Task<List<DockerVolumeDto>> GetDockerVolumesAsync(int serverId)
    {
        return await http.GetFromJsonAsync<List<DockerVolumeDto>>($"api/servers/{serverId}/docker/volumes", JsonOptions.Web) ?? [];
    }

    public Task<ApiStatus> PruneDockerAsync(int serverId, DockerPruneRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<DockerPruneRequest>($"api/servers/{serverId}/docker/prune", request, ct);

    public Task<ApiStatus> UpdateDockerResourceLimitsAsync(int serverId, DockerResourceLimitsRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<DockerResourceLimitsRequest>($"api/servers/{serverId}/docker/resource-limits", request, ct);

    public Task<ApiStatus> InspectContainerAsync(int serverId, string containerId, CancellationToken ct = default)
        => PostNoBodyAsync($"api/servers/{serverId}/docker/containers/{Uri.EscapeDataString(containerId)}/inspect", ct);

    public async Task<ApiStatus> GetComposeFileAsync(int serverId, string stackName, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"api/servers/{serverId}/docker/compose/{Uri.EscapeDataString(stackName)}/file", ct);
        return ApiStatus.From(response);
    }

    public Task<ApiStatus> SaveComposeFileAsync(int serverId, DockerComposeFileSaveRequest request, CancellationToken ct = default)
        => PutJsonNoBodyAsync<DockerComposeFileSaveRequest>($"api/servers/{serverId}/docker/compose/file", request, ct);

    public Task<ApiStatus> ExecuteShellCommandAsync(int serverId, DockerExecRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<DockerExecRequest>($"api/servers/{serverId}/docker/exec", request, ct);

    public Task<ApiStatus> GetContainerEnvVarsAsync(int serverId, string containerId, CancellationToken ct = default)
        => PostNoBodyAsync($"api/servers/{serverId}/docker/containers/{Uri.EscapeDataString(containerId)}/env", ct);

    public Task<ApiStatus> ListContainerFilesAsync(int serverId, DockerBrowseRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<DockerBrowseRequest>($"api/servers/{serverId}/docker/containers/browse", request, ct);

    public Task<ApiStatus> BuildImageAsync(int serverId, DockerBuildRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<DockerBuildRequest>($"api/servers/{serverId}/docker/build", request, ct);

    // --- Server Configuration ---
    public async Task<string?> ExportServerConfigAsync(int serverId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"api/servers/{serverId}/configuration/export", ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
    }

    public Task<ServerConfigValidationResult?> ValidateServerConfigAsync(int serverId, string yaml, CancellationToken ct = default)
        => PostJsonAsync<ServerConfigImportRequest, ServerConfigValidationResult>(
            $"api/servers/{serverId}/configuration/validate", new ServerConfigImportRequest { Yaml = yaml }, ct);

    public Task<ServerConfigPreviewDto?> PreviewServerConfigAsync(int serverId, string yaml, CancellationToken ct = default)
        => PostJsonAsync<ServerConfigImportRequest, ServerConfigPreviewDto>(
            $"api/servers/{serverId}/configuration/preview", new ServerConfigImportRequest { Yaml = yaml }, ct);

    public Task<ServerConfigDeployResultDto?> DeployServerConfigAsync(int serverId, string yaml, CancellationToken ct = default)
        => PostJsonAsync<ServerConfigImportRequest, ServerConfigDeployResultDto>(
            $"api/servers/{serverId}/configuration/deploy", new ServerConfigImportRequest { Yaml = yaml }, ct);
    public async Task<PaginatedResult<EnvironmentDto>> GetEnvironmentsAsync(int page = 1, int pageSize = 25, string? search = null, int? projectId = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrEmpty(search)) query["search"] = search;
        if (projectId.HasValue) query["projectId"] = projectId.Value.ToString();
        return await http.GetFromJsonAsync<PaginatedResult<EnvironmentDto>>(
            QueryHelpers.AddQueryString("api/environments", query), JsonOptions.Web) ?? new();
    }

    public async Task<EnvironmentDto?> GetEnvironmentAsync(int id, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<EnvironmentDto>($"api/environments/{id}", JsonOptions.Web, ct);
    }

    public async Task<List<EnvironmentDto>> GetAllEnvironmentsAsync(int? projectId = null)
    {
        const int pageSize = 100;
        var items = new List<EnvironmentDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetEnvironmentsAsync(page, pageSize, projectId: projectId);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public Task<EnvironmentDto?> CreateEnvironmentAsync(CreateEnvironmentRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateEnvironmentRequest, EnvironmentDto>("api/environments", request, ct);

    public Task<EnvironmentDto?> UpdateEnvironmentAsync(int id, UpdateEnvironmentRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateEnvironmentRequest, EnvironmentDto>($"api/environments/{id}", request, ct);

    public Task<ApiStatus> DeleteEnvironmentAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/environments/{id}", ct);

    public Task<EnvironmentDto?> DuplicateEnvironmentAsync(int id, DuplicateEnvironmentRequest request, CancellationToken ct = default)
        => PostJsonAsync<DuplicateEnvironmentRequest, EnvironmentDto>($"api/environments/{id}/duplicate", request, ct);

    public async Task<ApiStatus> LinkProjectServerToEnvironmentAsync(int envId, int projectServerId, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"api/environments/{envId}/project-servers/{projectServerId}", null, ct);
        return ApiStatus.From(response);
    }

    public Task<ApiStatus> UnlinkProjectServerFromEnvironmentAsync(int envId, int projectServerId, CancellationToken ct = default)
        => DeleteAsync($"api/environments/{envId}/project-servers/{projectServerId}", ct);
    // --- Git Light Repositories ---

    public async Task<PaginatedResult<GitLightRepoDto>> GetGitReposPageAsync(
        int page = 1, int pageSize = 25, int? projectId = null, string? search = null,
        string? sortBy = null, bool sortDescending = false, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString(),
            ["projectId"] = projectId?.ToString(),
            ["search"] = search,
            ["sortBy"] = sortBy,
            ["sortDescending"] = sortDescending.ToString()
        };
        return await GetJsonAsync<PaginatedResult<GitLightRepoDto>>(
            QueryHelpers.AddQueryString("api/git/repos", query), ct).ConfigureAwait(false) ?? new();
    }

    // projectId null => every repo the caller can read (across accessible projects); set => that project only.
    public async Task<List<GitLightRepoDto>> GetGitReposAsync(
        int? projectId = null, CancellationToken ct = default)
    {
        const int pageSize = 100;
        var items = new List<GitLightRepoDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetGitReposPageAsync(
                page, pageSize, projectId, ct: ct).ConfigureAwait(false);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public async Task<GitLightRepoDto?> GetGitRepoAsync(int id, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<GitLightRepoDto>($"api/git/repos/{id}", JsonOptions.Web, ct);
    }

    public Task<GitLightRepoDto?> CreateGitRepoAsync(CreateGitLightRepoRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateGitLightRepoRequest, GitLightRepoDto>("api/git/repos", request, ct);

    public Task<GitLightRepoDto?> UpdateGitRepoAsync(int id, UpdateGitLightRepoRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateGitLightRepoRequest, GitLightRepoDto>($"api/git/repos/{id}", request, ct);

    public Task<ApiStatus> DeleteGitRepoAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/git/repos/{id}", ct);

    // --- Git Light Commits ---

    public async Task<PaginatedResult<GitLightCommitDto>> GetGitCommitsAsync(int repoId, string? refName = null, int page = 1, int pageSize = 30, string? search = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrEmpty(refName)) query["ref"] = refName;
        if (!string.IsNullOrEmpty(search)) query["search"] = search;
        return await http.GetFromJsonAsync<PaginatedResult<GitLightCommitDto>>(
            QueryHelpers.AddQueryString($"api/git/repos/{repoId}/commits", query), JsonOptions.Web) ?? new();
    }

    public async Task<GitLightCommitDetailDto?> GetGitCommitDetailAsync(int repoId, string sha, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"api/git/repos/{repoId}/commits/{Uri.EscapeDataString(sha)}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GitLightCommitDetailDto>(JsonOptions.Web, ct);
    }

    // --- Git Light Branches ---

    public async Task<PaginatedResult<GitLightBranchDto>> GetGitBranchesPageAsync(
        int repoId, int page = 1, int pageSize = 25, string? search = null,
        string? sortBy = null, bool sortDescending = false, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await GetJsonAsync<PaginatedResult<GitLightBranchDto>>(
            QueryHelpers.AddQueryString($"api/git/repos/{repoId}/branches", query), ct).ConfigureAwait(false) ?? new();
    }

    public async Task<List<GitLightBranchDto>> GetGitBranchesAsync(
        int repoId, CancellationToken ct = default)
    {
        const int pageSize = 100;
        var items = new List<GitLightBranchDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetGitBranchesPageAsync(repoId, page, pageSize, ct: ct).ConfigureAwait(false);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public Task<ApiStatus> CreateGitBranchAsync(int repoId, CreateGitLightBranchRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<CreateGitLightBranchRequest>($"api/git/repos/{repoId}/branches", request, ct);

    public Task<ApiStatus> DeleteGitBranchAsync(int repoId, string name, CancellationToken ct = default)
        => DeleteAsync($"api/git/repos/{repoId}/branches/{Uri.EscapeDataString(name)}", ct);

    // --- Git Light Tags ---

    public async Task<PaginatedResult<GitLightTagDto>> GetGitTagsPageAsync(
        int repoId, int page = 1, int pageSize = 25, string? search = null,
        string? sortBy = null, bool sortDescending = false, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await GetJsonAsync<PaginatedResult<GitLightTagDto>>(
            QueryHelpers.AddQueryString($"api/git/repos/{repoId}/tags", query), ct).ConfigureAwait(false) ?? new();
    }

    public async Task<List<GitLightTagDto>> GetGitTagsAsync(
        int repoId, CancellationToken ct = default)
    {
        const int pageSize = 100;
        var items = new List<GitLightTagDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetGitTagsPageAsync(repoId, page, pageSize, ct: ct).ConfigureAwait(false);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public Task<ApiStatus> CreateGitTagAsync(int repoId, CreateGitLightTagRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<CreateGitLightTagRequest>($"api/git/repos/{repoId}/tags", request, ct);

    public Task<ApiStatus> DeleteGitTagAsync(int repoId, string name, CancellationToken ct = default)
        => DeleteAsync($"api/git/repos/{repoId}/tags/{Uri.EscapeDataString(name)}", ct);

    // --- Git Light File Browser ---

    public async Task<PaginatedResult<GitLightTreeEntryDto>> GetGitTreePageAsync(
        int repoId, string? refName = null, string? path = null,
        int page = 1, int pageSize = 25, string? search = null,
        string? sortBy = null, bool sortDescending = false, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        if (!string.IsNullOrEmpty(refName)) query["ref"] = refName;
        if (!string.IsNullOrEmpty(path)) query["path"] = path;
        return await GetJsonAsync<PaginatedResult<GitLightTreeEntryDto>>(
            QueryHelpers.AddQueryString($"api/git/repos/{repoId}/tree", query), ct).ConfigureAwait(false) ?? new();
    }

    public async Task<List<GitLightTreeEntryDto>> GetGitTreeAsync(
        int repoId, string? refName = null, string? path = null, CancellationToken ct = default)
    {
        const int pageSize = 100;
        var items = new List<GitLightTreeEntryDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetGitTreePageAsync(
                repoId, refName, path, page, pageSize, ct: ct).ConfigureAwait(false);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public async Task<GitLightBlobDto?> GetGitBlobAsync(int repoId, string refName, string path, CancellationToken ct = default)
    {
        var url = QueryHelpers.AddQueryString($"api/git/repos/{repoId}/blob",
            new Dictionary<string, string?> { ["ref"] = refName, ["path"] = path });
        return await http.GetFromJsonAsync<GitLightBlobDto>(url, JsonOptions.Web, ct);
    }

    public async Task<List<GitLightBlameLine>> GetGitBlameAsync(int repoId, string refName, string path)
    {
        var url = QueryHelpers.AddQueryString($"api/git/repos/{repoId}/blame",
            new Dictionary<string, string?> { ["ref"] = refName, ["path"] = path });
        return await http.GetFromJsonAsync<List<GitLightBlameLine>>(url, JsonOptions.Web) ?? [];
    }

    // --- Git Light Pull Requests ---

    public async Task<PaginatedResult<InternalPullRequestDto>> GetGitPullRequestsAsync(
        int repoId, int page = 1, int pageSize = 25, string? search = null, PullRequestStatus? status = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrEmpty(search)) query["search"] = search;
        if (status.HasValue) query["status"] = status.Value.ToString();
        return await http.GetFromJsonAsync<PaginatedResult<InternalPullRequestDto>>(
            QueryHelpers.AddQueryString($"api/git/repos/{repoId}/pull-requests", query), JsonOptions.Web) ?? new();
    }

    public async Task<InternalPullRequestDto?> GetGitPullRequestAsync(int repoId, int prNumber, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<InternalPullRequestDto>($"api/git/repos/{repoId}/pull-requests/{prNumber}", JsonOptions.Web, ct);
    }

    public Task<InternalPullRequestDto?> CreateGitPullRequestAsync(int repoId, CreateInternalPullRequestRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateInternalPullRequestRequest, InternalPullRequestDto>($"api/git/repos/{repoId}/pull-requests", request, ct);

    public Task<InternalPullRequestDto?> MergeGitPullRequestAsync(int repoId, int prNumber, CancellationToken ct = default)
        => PostNoBodyAsync<InternalPullRequestDto>($"api/git/repos/{repoId}/pull-requests/{prNumber}/merge", ct);

    public Task<InternalPullRequestDto?> CloseGitPullRequestAsync(int repoId, int prNumber, CancellationToken ct = default)
        => PostNoBodyAsync<InternalPullRequestDto>($"api/git/repos/{repoId}/pull-requests/{prNumber}/close", ct);

    public async Task<PullRequestDiffDto?> GetGitPullRequestDiffAsync(int repoId, int prNumber, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<PullRequestDiffDto>($"api/git/repos/{repoId}/pull-requests/{prNumber}/diff", JsonOptions.Web, ct);
    }

    // --- Branch Protection ---

    public async Task<string> GetGitCommitGraphAsync(int repoId, int maxCount = 100, CancellationToken ct = default)
    {
        return await http.GetStringAsync($"api/git/repos/{repoId}/graph?maxCount={maxCount}");
    }

    public async Task<List<GitLightCommitDto>> GetGitGraphDataAsync(int repoId, int maxCount = 120, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<List<GitLightCommitDto>>(
            $"api/git/repos/{repoId}/graph-data?maxCount={maxCount}", JsonOptions.Web, ct) ?? [];
    }

    public async Task<PaginatedResult<BranchProtectionRuleDto>> GetGitBranchProtectionRulesPageAsync(
        int repoId, int page = 1, int pageSize = 25, string? search = null,
        string? sortBy = null, bool sortDescending = false, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await GetJsonAsync<PaginatedResult<BranchProtectionRuleDto>>(
            QueryHelpers.AddQueryString($"api/git/repos/{repoId}/branch-protection", query), ct).ConfigureAwait(false) ?? new();
    }

    public async Task<List<BranchProtectionRuleDto>> GetGitBranchProtectionRulesAsync(
        int repoId, CancellationToken ct = default)
    {
        const int pageSize = 100;
        var items = new List<BranchProtectionRuleDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetGitBranchProtectionRulesPageAsync(
                repoId, page, pageSize, ct: ct).ConfigureAwait(false);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public Task<BranchProtectionRuleDto?> CreateGitBranchProtectionRuleAsync(int repoId, CreateBranchProtectionRuleRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateBranchProtectionRuleRequest, BranchProtectionRuleDto>($"api/git/repos/{repoId}/branch-protection", request, ct);

    public Task<BranchProtectionRuleDto?> UpdateGitBranchProtectionRuleAsync(int repoId, int ruleId, UpdateBranchProtectionRuleRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateBranchProtectionRuleRequest, BranchProtectionRuleDto>($"api/git/repos/{repoId}/branch-protection/{ruleId}", request, ct);

    public Task<ApiStatus> DeleteGitBranchProtectionRuleAsync(int repoId, int ruleId, CancellationToken ct = default)
        => DeleteAsync($"api/git/repos/{repoId}/branch-protection/{ruleId}", ct);
    // --- Git graph (commit / branch cross-linking) ---
    public async Task<GitCommitDto?> GetGitCommitAsync(int id, CancellationToken ct = default)
        => await http.GetFromJsonAsync<GitCommitDto>($"api/gitgraph/commits/{id}", JsonOptions.Web, ct);

    public async Task<GitBranchDto?> GetGitBranchAsync(int id, CancellationToken ct = default)
        => await http.GetFromJsonAsync<GitBranchDto>($"api/gitgraph/branches/{id}", JsonOptions.Web, ct);
    /// <summary>POST a JSON body, deserialize the JSON response. Returns <c>default</c> on non-2xx.</summary>
    private async Task<TRes?> PostJsonAsync<TReq, TRes>(string url, TReq body, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(url, body, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<TRes>(JsonOptions.Web, ct).ConfigureAwait(false);
    }

    /// <summary>PUT a JSON body, deserialize the JSON response. Returns <c>default</c> on non-2xx.</summary>
    private async Task<TRes?> PutJsonAsync<TReq, TRes>(string url, TReq body, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync(url, body, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<TRes>(JsonOptions.Web, ct).ConfigureAwait(false);
    }

    /// <summary>POST a JSON body, return the request status without parsing the response.</summary>
    private async Task<ApiStatus> PostJsonNoBodyAsync<TReq>(string url, TReq body, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(url, body, ct).ConfigureAwait(false);
        return ApiStatus.From(response);
    }

    /// <summary>PUT a JSON body, return the request status without parsing the response.</summary>
    private async Task<ApiStatus> PutJsonNoBodyAsync<TReq>(string url, TReq body, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync(url, body, ct).ConfigureAwait(false);
        return ApiStatus.From(response);
    }

    /// <summary>GET + JSON deserialize, with an optional query-string map. Returns <c>default</c> on 404 / null.</summary>
    private Task<TRes?> GetJsonAsync<TRes>(string url, CancellationToken ct = default)
        => http.GetFromJsonAsync<TRes>(url, JsonOptions.Web, ct);

    /// <summary>GET + JSON deserialize with a query-string dictionary appended to the URL.</summary>
    private Task<TRes?> GetJsonAsync<TRes>(string url, IDictionary<string, string?> query, CancellationToken ct = default)
        => http.GetFromJsonAsync<TRes>(QueryHelpers.AddQueryString(url, query), JsonOptions.Web, ct);

    /// <summary>DELETE returning the request status.</summary>
    private async Task<ApiStatus> DeleteAsync(string url, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync(url, ct).ConfigureAwait(false);
        return ApiStatus.From(response);
    }

    /// <summary>POST with no body, deserialize the JSON response. Returns <c>default</c> on non-2xx.</summary>
    private async Task<TRes?> PostNoBodyAsync<TRes>(string url, CancellationToken ct = default)
    {
        var response = await http.PostAsync(url, null, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<TRes>(JsonOptions.Web, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// POST (optionally with a JSON body) mapping the response into an <see cref="ApiOutcome{TValue,TError}"/>:
    /// 2xx → <c>Value</c>, 400 → typed <c>Error</c> body, 404 → <c>NotFound</c>. Use for endpoints that
    /// return a typed validation/problem payload the UI must surface.
    /// </summary>
    private async Task<ApiOutcome<TValue, TError>> PostForOutcomeAsync<TValue, TError>(
        string url, object? body = null, CancellationToken ct = default)
        where TValue : class
        where TError : class
    {
        using var content = body is null ? null : JsonContent.Create(body, body.GetType());
        var response = await http.PostAsync(url, content, ct).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
            return new(await response.Content.ReadFromJsonAsync<TValue>(JsonOptions.Web, ct).ConfigureAwait(false), null, false);

        if (response.StatusCode == HttpStatusCode.BadRequest)
            return new(null, await response.Content.ReadFromJsonAsync<TError>(JsonOptions.Web, ct).ConfigureAwait(false), false, response.StatusCode);

        return new(null, null, response.StatusCode == HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>PUT a JSON body, mapping the response into an <see cref="ApiOutcome{TValue,TError}"/>
    /// (2xx → <c>Value</c>, 400 → typed <c>Error</c>, 404 → <c>NotFound</c>).</summary>
    private async Task<ApiOutcome<TValue, TError>> PutForOutcomeAsync<TValue, TError>(
        string url, object body, CancellationToken ct = default)
        where TValue : class
        where TError : class
    {
        using var content = JsonContent.Create(body, body.GetType());
        var response = await http.PutAsync(url, content, ct).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
            return new(await response.Content.ReadFromJsonAsync<TValue>(JsonOptions.Web, ct).ConfigureAwait(false), null, false);

        if (response.StatusCode == HttpStatusCode.BadRequest)
            return new(null, await response.Content.ReadFromJsonAsync<TError>(JsonOptions.Web, ct).ConfigureAwait(false), false, response.StatusCode);

        return new(null, null, response.StatusCode == HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>POST with no body, returns the request status. Used for trigger/sync/promote-style endpoints.</summary>
    private async Task<ApiStatus> PostNoBodyAsync(string url, CancellationToken ct = default)
    {
        var response = await http.PostAsync(url, null, ct).ConfigureAwait(false);
        return ApiStatus.From(response);
    }

    /// <summary>
    /// POST a JSON body whose .NET type is only known at runtime (e.g. an anonymous type).
    /// Required because the strongly-typed <see cref="PostJsonAsync{TReq,TRes}"/> serializes against
    /// <c>TReq</c>; with <c>TReq = object</c> System.Text.Json would skip the runtime properties.
    /// </summary>
    private async Task<TRes?> PostJsonAsync<TRes>(string url, object body, CancellationToken ct = default)
    {
        using var content = JsonContent.Create(body, body.GetType());
        var response = await http.PostAsync(url, content, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<TRes>(JsonOptions.Web, ct).ConfigureAwait(false);
    }

    /// <summary>POST a runtime-typed JSON body, return the request status without parsing the response.</summary>
    private async Task<ApiStatus> PostJsonNoBodyAsync(string url, object body, CancellationToken ct = default)
    {
        using var content = JsonContent.Create(body, body.GetType());
        var response = await http.PostAsync(url, content, ct).ConfigureAwait(false);
        return ApiStatus.From(response);
    }
    // --- Mail ---
    public async Task<MailDataDto> GetMailStateAsync(int serverId, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<MailDataDto>($"api/servers/{serverId}/mail", JsonOptions.Web, ct) ?? new();
    }

    public async Task<PaginatedResult<MailDomainDto>> GetMailDomainsPageAsync(
        int serverId, int page, int pageSize, string? search = null, string? sortBy = null, bool sortDescending = false)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await http.GetFromJsonAsync<PaginatedResult<MailDomainDto>>(
            QueryHelpers.AddQueryString($"api/servers/{serverId}/mail/domains", query), JsonOptions.Web) ?? new();
    }

    public async Task<List<MailDomainDto>> GetMailDomainsAsync(int serverId)
    {
        const int pageSize = 100;
        var items = new List<MailDomainDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetMailDomainsPageAsync(serverId, page, pageSize);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public async Task<MailDomainDto?> GetMailDomainAsync(int serverId, int domainId, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<MailDomainDto>($"api/servers/{serverId}/mail/domains/{domainId}", JsonOptions.Web, ct);
    }

    public Task<MailDomainDto?> CreateMailDomainAsync(int serverId, CreateMailDomainRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateMailDomainRequest, MailDomainDto>($"api/servers/{serverId}/mail/domains", request, ct);

    public Task<MailDomainDto?> UpdateMailDomainAsync(int serverId, int domainId, UpdateMailDomainRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateMailDomainRequest, MailDomainDto>($"api/servers/{serverId}/mail/domains/{domainId}", request, ct);

    public Task<ApiStatus> DeleteMailDomainAsync(int serverId, int domainId, CancellationToken ct = default)
        => DeleteAsync($"api/servers/{serverId}/mail/domains/{domainId}", ct);

    public async Task<PaginatedResult<MailAccountDto>> GetMailAccountsPageAsync(
        int serverId, int page, int pageSize, string? search = null, string? sortBy = null, bool sortDescending = false)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await http.GetFromJsonAsync<PaginatedResult<MailAccountDto>>(
            QueryHelpers.AddQueryString($"api/servers/{serverId}/mail/accounts", query), JsonOptions.Web) ?? new();
    }

    public async Task<List<MailAccountDto>> GetMailAccountsAsync(int serverId)
    {
        const int pageSize = 100;
        var items = new List<MailAccountDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetMailAccountsPageAsync(serverId, page, pageSize);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public Task<MailAccountDto?> CreateMailAccountAsync(int serverId, CreateMailAccountRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateMailAccountRequest, MailAccountDto>($"api/servers/{serverId}/mail/accounts", request, ct);

    public Task<MailAccountDto?> UpdateMailAccountAsync(int serverId, int accountId, UpdateMailAccountRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateMailAccountRequest, MailAccountDto>($"api/servers/{serverId}/mail/accounts/{accountId}", request, ct);

    public Task<ApiStatus> DeleteMailAccountAsync(int serverId, int accountId, CancellationToken ct = default)
        => DeleteAsync($"api/servers/{serverId}/mail/accounts/{accountId}", ct);

    public Task<ApiStatus> ExecuteMailActionAsync(int serverId, MailActionRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<MailActionRequest>($"api/servers/{serverId}/mail/action", request, ct);

    public Task<ApiStatus> GetMailLogsAsync(int serverId, MailLogRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<MailLogRequest>($"api/servers/{serverId}/mail/logs", request, ct);

    public Task<ApiStatus> SetupMailAsync(int serverId, MailSetupRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<MailSetupRequest>($"api/servers/{serverId}/mail/setup", request, ct);

    public async Task<MailDnsRecordsDto?> GetMailDnsRecordsAsync(int serverId, int domainId, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<MailDnsRecordsDto>($"api/servers/{serverId}/mail/domains/{domainId}/dns", JsonOptions.Web, ct);
    }

    // --- Mail aliases ---

    public async Task<PaginatedResult<MailAliasDto>> GetMailAliasesPageAsync(
        int serverId, int page, int pageSize, string? search = null, string? sortBy = null, bool sortDescending = false)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await http.GetFromJsonAsync<PaginatedResult<MailAliasDto>>(
            QueryHelpers.AddQueryString($"api/servers/{serverId}/mail/aliases", query), JsonOptions.Web) ?? new();
    }

    public async Task<List<MailAliasDto>> GetMailAliasesAsync(int serverId)
    {
        const int pageSize = 100;
        var items = new List<MailAliasDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetMailAliasesPageAsync(serverId, page, pageSize);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public Task<MailAliasDto?> CreateMailAliasAsync(int serverId, int domainId, CreateMailAliasRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateMailAliasRequest, MailAliasDto>($"api/servers/{serverId}/mail/domains/{domainId}/aliases", request, ct);

    public Task<MailAliasDto?> UpdateMailAliasAsync(int serverId, int aliasId, UpdateMailAliasRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateMailAliasRequest, MailAliasDto>($"api/servers/{serverId}/mail/aliases/{aliasId}", request, ct);

    public Task<ApiStatus> DeleteMailAliasAsync(int serverId, int aliasId, CancellationToken ct = default)
        => DeleteAsync($"api/servers/{serverId}/mail/aliases/{aliasId}", ct);

    // --- DKIM key rotation ---

    public Task<DkimRotationResultDto?> RotateMailDkimKeyAsync(int serverId, int domainId, DkimRotationRequest request, CancellationToken ct = default)
        => PostJsonAsync<DkimRotationRequest, DkimRotationResultDto>($"api/servers/{serverId}/mail/domains/{domainId}/dkim/rotate", request, ct);
    // --- Module Links ---
    public async Task<List<ModuleLinkDto>> GetModuleLinksAsync(int serverId)
    {
        return await http.GetFromJsonAsync<List<ModuleLinkDto>>($"api/servers/{serverId}/module-links", JsonOptions.Web) ?? [];
    }

    public async Task<List<LinkedResourceDto>> GetLinksForResourceAsync(int serverId, ModuleLinkType sourceType, string sourceIdentifier)
    {
        return await http.GetFromJsonAsync<List<LinkedResourceDto>>(
            $"api/servers/{serverId}/module-links/resource?sourceType={sourceType}&sourceIdentifier={Uri.EscapeDataString(sourceIdentifier)}", JsonOptions.Web) ?? [];
    }

    public async Task<PaginatedResult<LinkedResourceDto>> GetModuleLinksPageAsync(
        int serverId, ModuleLinkPageRequest request, CancellationToken ct = default)
    {
        return await PostJsonAsync<ModuleLinkPageRequest, PaginatedResult<LinkedResourceDto>>(
            $"api/servers/{serverId}/module-links/resource-page", request, ct)
            ?? new PaginatedResult<LinkedResourceDto>();
    }

    public Task<ModuleLinkDto?> CreateModuleLinkAsync(int serverId, CreateModuleLinkRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateModuleLinkRequest, ModuleLinkDto>($"api/servers/{serverId}/module-links", request, ct);

    public Task<ApiStatus> DeleteModuleLinkAsync(int serverId, int linkId, CancellationToken ct = default)
        => DeleteAsync($"api/servers/{serverId}/module-links/{linkId}", ct);

    public Task<ApiStatus> AutoDetectModuleLinksAsync(int serverId, CancellationToken ct = default)
        => PostNoBodyAsync($"api/servers/{serverId}/module-links/auto-detect", ct);
    // S-FEAT-27: fetch the raw OpenAPI document for the in-app API Reference page.
    public async Task<JsonDocument?> GetOpenApiSpecAsync(CancellationToken ct = default)
    {
        var json = await http.GetStringAsync("openapi/v1.json", ct).ConfigureAwait(false);
        return JsonDocument.Parse(json);
    }
    public async Task<PaginatedResult<OrganizationDto>?> GetOrganizationsAsync(
        string? search, int page, int pageSize, string? sortBy = null,
        bool sortDescending = false, CancellationToken ct = default)
    {
        var qs = $"?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search)) qs += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(sortBy)) qs += $"&sortBy={Uri.EscapeDataString(sortBy)}&sortDescending={sortDescending.ToString().ToLowerInvariant()}";
        return await http.GetFromJsonAsync<PaginatedResult<OrganizationDto>>($"api/organizations{qs}", JsonOptions.Web, ct);
    }

    public async Task<OrganizationDetailDto?> GetOrganizationAsync(int id, CancellationToken ct = default)
        => await http.GetFromJsonAsync<OrganizationDetailDto>($"api/organizations/{id}", JsonOptions.Web, ct);

    public Task<OrganizationDto?> CreateOrganizationAsync(CreateOrganizationRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateOrganizationRequest, OrganizationDto>("api/organizations", request, ct);

    public Task<OrganizationDto?> UpdateOrganizationAsync(int id, UpdateOrganizationRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateOrganizationRequest, OrganizationDto>($"api/organizations/{id}", request, ct);

    public Task<ApiStatus> DeleteOrganizationAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/organizations/{id}", ct);

    public Task<OrganizationMemberDto?> AddOrganizationMemberAsync(int id, AddOrganizationMemberRequest request, CancellationToken ct = default)
        => PostJsonAsync<AddOrganizationMemberRequest, OrganizationMemberDto>($"api/organizations/{id}/members", request, ct);

    public Task<OrganizationMemberDto?> UpdateOrganizationMemberAsync(int id, int memberId, UpdateOrganizationMemberRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateOrganizationMemberRequest, OrganizationMemberDto>($"api/organizations/{id}/members/{memberId}", request, ct);

    public Task<ApiStatus> RemoveOrganizationMemberAsync(int id, int memberId, CancellationToken ct = default)
        => DeleteAsync($"api/organizations/{id}/members/{memberId}", ct);

    public async Task<List<UserOrganizationDto>> GetUserOrganizationsAsync(int userId, CancellationToken ct = default)
        => await http.GetFromJsonAsync<List<UserOrganizationDto>>($"api/users/{userId}/organizations", JsonOptions.Web, ct) ?? [];

    public Task<ApiStatus> AssignOrganizationProjectsAsync(int id, AssignProjectsRequest request, CancellationToken ct = default)
        => PutJsonNoBodyAsync<AssignProjectsRequest>($"api/organizations/{id}/projects", request, ct);

    /// <summary>Returns the organizations the current user is a member of (header picker).</summary>
    public async Task<List<MyOrganizationDto>?> GetMyOrganizationsAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<List<MyOrganizationDto>>("api/organizations/me", JsonOptions.Web, ct);
    // --- Pipelines ---
    public async Task<PaginatedResult<PipelineDto>> GetPipelinesAsync(int page = 1, int pageSize = 25, string? search = null, PipelineTriggerType? triggerType = null, int? environmentId = null, int? projectServerId = null, int? projectId = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrEmpty(search)) query["search"] = search;
        if (triggerType.HasValue) query["triggerType"] = triggerType.Value.ToString();
        if (environmentId.HasValue) query["environmentId"] = environmentId.Value.ToString();
        if (projectServerId.HasValue) query["projectServerId"] = projectServerId.Value.ToString();
        if (projectId.HasValue) query["projectId"] = projectId.Value.ToString();
        return await http.GetFromJsonAsync<PaginatedResult<PipelineDto>>(
            QueryHelpers.AddQueryString("api/pipelines", query), JsonOptions.Web) ?? new();
    }

    public async Task<PipelineDependencyGroupsDto> GetPipelineDependencyGroupsAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<PipelineDependencyGroupsDto>("api/pipelines/dependencies", JsonOptions.Web, ct) ?? new();

    public async Task<List<PipelineRunDto>> GetActivePipelineRunsAsync(int? projectId = null, CancellationToken ct = default)
    {
        var uri = projectId.HasValue
            ? $"api/pipelines/runs/active?projectId={projectId.Value}"
            : "api/pipelines/runs/active";
        return await http.GetFromJsonAsync<List<PipelineRunDto>>(uri, JsonOptions.Web, ct) ?? [];
    }

    public async Task<List<PipelineRunDto>> GetRecentPipelineRunsAsync(int? projectId = null, CancellationToken ct = default)
    {
        var uri = projectId.HasValue
            ? $"api/pipelines/runs/recent?projectId={projectId.Value}"
            : "api/pipelines/runs/recent";
        return await http.GetFromJsonAsync<List<PipelineRunDto>>(uri, JsonOptions.Web, ct) ?? [];
    }

    public async Task<PaginatedResult<PipelineDependencyDto>> GetPipelineDependencyPageAsync(
        int page, int pageSize, string? search = null, PipelineTriggerType? triggerType = null,
        int? projectId = null, int? serverId = null, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrWhiteSpace(search)) query["search"] = search;
        if (triggerType.HasValue) query["triggerType"] = triggerType.Value.ToString();
        if (projectId.HasValue) query["projectId"] = projectId.Value.ToString();
        if (serverId.HasValue) query["serverId"] = serverId.Value.ToString();
        var uri = QueryHelpers.AddQueryString("api/pipelines/dependencies/page", query);
        using var response = await http.GetAsync(uri, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Rolling-deployment compatibility: an older backend only exposes the unpaged graph.
            var legacy = await GetPipelineDependencyGroupsAsync(ct);
            HashSet<int>? serverPipelineIds = null;
            if (serverId.HasValue)
                serverPipelineIds = (await GetServerPipelinesAsync(serverId.Value)).Select(item => item.Id).ToHashSet();
            var filtered = legacy.Parents.Concat(legacy.Leaves)
                .Where(item => !projectId.HasValue || item.ProjectId == projectId)
                .Where(item => serverPipelineIds is null || serverPipelineIds.Contains(item.Id))
                .Where(item => string.IsNullOrWhiteSpace(search)
                    || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || (item.ProjectName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
                .Where(item => !triggerType.HasValue || item.TriggerType == triggerType)
                .OrderBy(item => item.Name).ToList();
            return new PaginatedResult<PipelineDependencyDto>
            {
                Items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                TotalCount = filtered.Count,
                Page = page,
                PageSize = pageSize
            };
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PaginatedResult<PipelineDependencyDto>>(JsonOptions.Web, ct) ?? new();
    }

    public async Task<PipelineDto?> GetPipelineAsync(int id, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<PipelineDto>($"api/pipelines/{id}", JsonOptions.Web, ct);
    }

    public async Task<PipelineSourceDto?> GetPipelineSourceAsync(int id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"api/pipelines/{id}/source", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<PipelineSourceDto>(JsonOptions.Web, ct);
    }

    public Task<ApiOutcome<PipelineDto, YamlValidationResultDto>> CreatePipelineAsync(
        CreatePipelineRequest request, CancellationToken ct = default)
        => PostForOutcomeAsync<PipelineDto, YamlValidationResultDto>("api/pipelines", request, ct);

    public Task<ApiOutcome<PipelineDto, YamlValidationResultDto>> UpdatePipelineAsync(
        int id, UpdatePipelineRequest request, CancellationToken ct = default)
        => PutForOutcomeAsync<PipelineDto, YamlValidationResultDto>($"api/pipelines/{id}", request, ct);

    public Task<ApiStatus> DeletePipelineAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/pipelines/{id}", ct);

    // Optional queue-time parameters and a per-run source-branch override (validated server-side).
    public Task<ApiOutcome<PipelineRunDto, YamlValidationResultDto>> TriggerPipelineRunAsync(
        int id, Dictionary<string, string>? parameters = null, string? sourceBranch = null, CancellationToken ct = default)
    {
        // Expand-contract wire shape: old backends expect a top-level dictionary. New backends split the
        // reserved branch key back into PipelineRunRequest.SourceBranch, so both deployment colours work.
        var body = parameters is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(sourceBranch))
            body["AETHEUS_RUN_BRANCH"] = sourceBranch;
        return PostForOutcomeAsync<PipelineRunDto, YamlValidationResultDto>(
            $"api/pipelines/{id}/run", body, ct);
    }

    // P: the queue-time parameters declared by the pipeline's authoritative (git-first) YAML.
    public async Task<List<PipelineRunParameterDto>> GetPipelineRunParametersAsync(int id, string? sourceBranch = null, CancellationToken ct = default)
    {
        var uri = $"api/pipelines/{id}/parameters";
        if (!string.IsNullOrWhiteSpace(sourceBranch)) uri += $"?sourceBranch={Uri.EscapeDataString(sourceBranch)}";
        return await http.GetFromJsonAsync<List<PipelineRunParameterDto>>(uri, JsonOptions.Web, ct) ?? [];
    }

    public Task<ApiOutcome<PipelinePreflightDto, YamlValidationResultDto>> PreflightPipelineAsync(
        int id, string? sourceBranch = null, CancellationToken ct = default)
    {
        var uri = $"api/pipelines/{id}/preflight";
        if (!string.IsNullOrWhiteSpace(sourceBranch))
            uri += $"?sourceBranch={Uri.EscapeDataString(sourceBranch)}";
        return PostForOutcomeAsync<PipelinePreflightDto, YamlValidationResultDto>(uri, null, ct);
    }

    public Task<PipelineRunDto?> RetryFailedStepsAsync(int runId, CancellationToken ct = default)
        => PostNoBodyAsync<PipelineRunDto>($"api/pipelines/runs/{runId}/retry-failed", ct);

    // G: re-launch a run - current definition, snapshot @ same commit, or snapshot @ branch head.
    public Task<PipelineRunDto?> RerunPipelineRunAsync(int runId, RerunMode mode, CancellationToken ct = default)
        => PostNoBodyAsync<PipelineRunDto>($"api/pipelines/runs/{runId}/rerun?mode={mode}", ct);

    // Cancel an in-progress run (204 on success; 404 when the run is already terminal / unknown).
    public Task<ApiStatus> CancelPipelineRunAsync(int runId, CancellationToken ct = default)
        => PostNoBodyAsync($"api/pipelines/runs/{runId}/cancel", ct);

    public Task<DryRunResultDto?> DryRunPipelineAsync(int id, Dictionary<string, string>? additionalVars = null, CancellationToken ct = default)
        => PostJsonAsync<Dictionary<string, string>?, DryRunResultDto>($"api/pipelines/{id}/dry-run", additionalVars, ct);

    public async Task<List<PipelineTemplateSummaryDto>> GetPipelineTemplatesAsync()
    {
        if (_templateCache is not null) return _templateCache;
        _templateCache = await http.GetFromJsonAsync<List<PipelineTemplateSummaryDto>>("api/pipelines/templates", JsonOptions.Web) ?? [];
        return _templateCache;
    }

    public async Task<PaginatedResult<PipelineFleetItemDto>> GetPipelineFleetAsync(
        int page, int pageSize, string? search = null, int? templateId = null,
        int? projectId = null, PipelineFleetFreshness? freshness = null,
        string? sortBy = null, bool sortDescending = false,
        CancellationToken ct = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search)}");
        if (templateId is not null) query.Add($"templateId={templateId}");
        if (projectId is not null) query.Add($"projectId={projectId}");
        if (freshness is not null) query.Add($"freshness={freshness}");
        if (!string.IsNullOrWhiteSpace(sortBy))
        {
            query.Add($"sortBy={Uri.EscapeDataString(sortBy)}");
            query.Add($"sortDescending={sortDescending.ToString().ToLowerInvariant()}");
        }
        return await http.GetFromJsonAsync<PaginatedResult<PipelineFleetItemDto>>(
            $"api/pipelines/fleet?{string.Join('&', query)}", JsonOptions.Web, ct)
            ?? new PaginatedResult<PipelineFleetItemDto>();
    }

    public Task<PipelineFleetItemDto?> GetPipelineFleetItemAsync(
        int pipelineId, CancellationToken ct = default) =>
        http.GetFromJsonAsync<PipelineFleetItemDto>(
            $"api/pipelines/{pipelineId}/fleet-item", JsonOptions.Web, ct);

    public Task<PipelineFleetUpdatePreviewDto?> PreviewPipelineFleetUpdateAsync(
        int pipelineId, int targetVersion, CancellationToken ct = default) =>
        PostJsonAsync<PipelineFleetUpdateRequest, PipelineFleetUpdatePreviewDto>(
            $"api/pipelines/{pipelineId}/fleet-update/preview",
            new PipelineFleetUpdateRequest { TargetVersion = targetVersion }, ct);

    public Task<PipelineDto?> ApplyPipelineFleetUpdateAsync(
        int pipelineId, PipelineFleetUpdateRequest request, CancellationToken ct = default) =>
        PostJsonAsync<PipelineFleetUpdateRequest, PipelineDto>(
            $"api/pipelines/{pipelineId}/fleet-update", request, ct);

    public Task<PipelineTemplateDto?> ExtractPipelineTemplateAsync(
        int pipelineId, ExtractPipelineTemplateRequest request, CancellationToken ct = default) =>
        PostJsonAsync<ExtractPipelineTemplateRequest, PipelineTemplateDto>(
            $"api/pipelines/{pipelineId}/extract-template", request, ct);

    public Task<PipelineTemplateDto?> PromotePipelineTemplateAsync(
        int pipelineId, PromotePipelineTemplateRequest request, CancellationToken ct = default) =>
        PostJsonAsync<PromotePipelineTemplateRequest, PipelineTemplateDto>(
            $"api/pipelines/{pipelineId}/promote-template", request, ct);

    public Task<PipelinePromotePreviewDto?> GetPipelinePromotePreviewAsync(
        int pipelineId, CancellationToken ct = default) =>
        http.GetFromJsonAsync<PipelinePromotePreviewDto>(
            $"api/pipelines/{pipelineId}/promote-template/preview", JsonOptions.Web, ct);

    public void InvalidateTemplateCache() => _templateCache = null;

    public async Task<PipelineTemplateDto?> GetPipelineTemplateAsync(int id, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<PipelineTemplateDto>($"api/pipelines/templates/{id}", JsonOptions.Web, ct);
    }

    public async Task<PaginatedResult<PipelineTemplateVersionSummaryDto>> GetPipelineTemplateVersionsAsync(
        int id, int page, int pageSize, string? sortBy = null, bool sortDescending = true,
        CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, null, sortBy, sortDescending);
        return await http.GetFromJsonAsync<PaginatedResult<PipelineTemplateVersionSummaryDto>>(
            QueryHelpers.AddQueryString($"api/pipelines/templates/{id}/versions", query),
            JsonOptions.Web, ct) ?? new();
    }

    public async Task<PipelineTemplateVersionDto?> GetPipelineTemplateVersionAsync(
        int id, int version, CancellationToken ct = default)
        => await http.GetFromJsonAsync<PipelineTemplateVersionDto>(
            $"api/pipelines/templates/{id}/versions/{version}", JsonOptions.Web, ct);

    public Task<string?> ResolvePipelineTemplateAsync(
        int id, int version, CancellationToken ct = default) =>
        PostJsonAsync<Dictionary<string, string>?, string>(
            $"api/pipelines/templates/{id}/resolve?version={version}", null, ct);

    public async Task<PipelineTemplateDto?> CreatePipelineTemplateAsync(CreatePipelineTemplateRequest request, CancellationToken ct = default)
    {
        var dto = await PostJsonAsync<CreatePipelineTemplateRequest, PipelineTemplateDto>("api/pipelines/templates", request, ct).ConfigureAwait(false);
        if (dto != null) InvalidateTemplateCache();
        return dto;
    }

    public async Task<PipelineTemplateDto?> UpdatePipelineTemplateAsync(int id, UpdatePipelineTemplateRequest request, CancellationToken ct = default)
    {
        var dto = await PutJsonAsync<UpdatePipelineTemplateRequest, PipelineTemplateDto>($"api/pipelines/templates/{id}", request, ct).ConfigureAwait(false);
        if (dto != null) InvalidateTemplateCache();
        return dto;
    }

    public async Task<ApiStatus> DeletePipelineTemplateAsync(int id, CancellationToken ct = default)
    {
        var ok = await DeleteAsync($"api/pipelines/templates/{id}", ct).ConfigureAwait(false);
        if (ok) InvalidateTemplateCache();
        return ok;
    }

    public async Task<byte[]?> ExportPipelineTemplateAsync(int id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"api/pipelines/templates/{id}/export", ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<PipelineTemplateDto?> ImportPipelineTemplateAsync(byte[] fileContent, string fileName, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(fileContent), "file", fileName);
        var response = await http.PostAsync("api/pipelines/templates/import", content, ct);
        if (!response.IsSuccessStatusCode) return null;
        InvalidateTemplateCache();
        return await response.Content.ReadFromJsonAsync<PipelineTemplateDto>(JsonOptions.Web);
    }

    public async Task<List<PipelineRunDto>> GetPipelineRunsAsync(int pipelineId)
    {
        // F12: backend now returns PaginatedResult; default to first page (server clamps).
        var paged = await http.GetFromJsonAsync<PaginatedResult<PipelineRunDto>>($"api/pipelines/{pipelineId}/runs?pageSize=50", JsonOptions.Web);
        return paged?.Items ?? [];
    }

    public async Task<PaginatedResult<PipelineRunDto>> GetPipelineRunsPagedAsync(int pipelineId, int page = 1, int pageSize = 25)
    {
        return await http.GetFromJsonAsync<PaginatedResult<PipelineRunDto>>(
            $"api/pipelines/{pipelineId}/runs?page={page}&pageSize={pageSize}", JsonOptions.Web) ?? new PaginatedResult<PipelineRunDto>();
    }

    public async Task<PipelineRunDto?> GetPipelineRunAsync(int runId, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<PipelineRunDto>($"api/pipelines/runs/{runId}", JsonOptions.Web, ct);
    }

    public async Task<PipelineCoverageSummaryDto?> GetCoverageSummaryAsync(int runId, CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<PipelineCoverageSummaryDto>($"api/pipelines/runs/{runId}/coverage", JsonOptions.Web, ct); }
        catch (HttpRequestException) { return null; }
    }

    public async Task<List<CoverageTrendPointDto>> GetCoverageTrendAsync(int runId, int take = 15, CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<CoverageTrendPointDto>>($"api/pipelines/runs/{runId}/coverage-trend?take={take}", JsonOptions.Web, ct) ?? []; }
        catch (HttpRequestException) { return []; }
    }

    public async Task<List<ComplexityTrendPointDto>> GetComplexityTrendAsync(int runId, int take = 15, CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<ComplexityTrendPointDto>>($"api/pipelines/runs/{runId}/metrics-trend?take={take}", JsonOptions.Web, ct) ?? []; }
        catch (HttpRequestException) { return []; }
    }

    public async Task<ProjectQualityTrendDto> GetProjectQualityTrendAsync(int projectId, int take = 15, CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<ProjectQualityTrendDto>($"api/pipelines/projects/{projectId}/quality-trend?take={take}", JsonOptions.Web, ct) ?? new(); }
        catch (HttpRequestException) { return new(); }
    }

    // --- Work Items (kanban) ---
    public async Task<List<WorkItemBoardColumn>> GetWorkItemBoardAsync(int projectId)
    {
        return await http.GetFromJsonAsync<List<WorkItemBoardColumn>>($"api/work-items/board/{projectId}", JsonOptions.Web) ?? [];
    }

    public Task<WorkItemDto?> CreateWorkItemAsync(CreateWorkItemRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateWorkItemRequest, WorkItemDto>("api/work-items", request, ct);

    public Task<WorkItemDto?> UpdateWorkItemAsync(int id, UpdateWorkItemRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateWorkItemRequest, WorkItemDto>($"api/work-items/{id}", request, ct);

    public async Task<WorkItemDto?> MoveWorkItemAsync(int id, MoveWorkItemRequest request)
    {
        var response = await http.PatchAsJsonAsync($"api/work-items/{id}/move", request, JsonOptions.Web);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<WorkItemDto>(JsonOptions.Web);
    }

    public Task<ApiStatus> DeleteWorkItemAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/work-items/{id}", ct);

    // --- External Repos (Features:ExternalRepos) ---
    public async Task<bool> IsExternalReposEnabledAsync()
    {
        try { return await http.GetFromJsonAsync<bool>("api/external-repos/enabled", JsonOptions.Web); }
        catch (HttpRequestException) { return false; }
    }

    public async Task<ExternalRepoDto?> GetExternalRepoAsync(int projectId)
    {
        var response = await http.GetAsync($"api/external-repos/project/{projectId}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ExternalRepoDto>(JsonOptions.Web);
    }

    public Task<ExternalRepoDto?> AttachExternalRepoAsync(AttachExternalRepoRequest request, CancellationToken ct = default)
        => PostJsonAsync<AttachExternalRepoRequest, ExternalRepoDto>("api/external-repos/attach", request, ct);

    public async Task<ExternalRepoDto?> SyncExternalRepoNowAsync(int projectId)
    {
        var response = await http.PostAsync($"api/external-repos/project/{projectId}/sync", null);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<ExternalRepoDto>(JsonOptions.Web);
    }

    public Task<ApiStatus> DetachExternalRepoAsync(int projectId, CancellationToken ct = default)
        => DeleteAsync($"api/external-repos/project/{projectId}", ct);

    // --- Package Feeds ---
    public async Task<PaginatedResult<PackageFeedDto>> GetPackageFeedsAsync(
        int page, int pageSize, string? search = null, string? sortBy = null,
        bool sortDescending = false, CancellationToken ct = default)
    {
        var qs = $"?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search)) qs += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(sortBy)) qs += $"&sortBy={Uri.EscapeDataString(sortBy)}&sortDescending={sortDescending.ToString().ToLowerInvariant()}";
        return await http.GetFromJsonAsync<PaginatedResult<PackageFeedDto>>(
            $"api/package-feeds{qs}", JsonOptions.Web, ct) ?? new PaginatedResult<PackageFeedDto>();
    }

    public async Task<PaginatedResult<CoverageAssemblyDto>> GetCoverageAssembliesAsync(
        int runId, int page, int pageSize, string? search = null, string? sortBy = null,
        bool sortDescending = false, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await http.GetFromJsonAsync<PaginatedResult<CoverageAssemblyDto>>(
            QueryHelpers.AddQueryString($"api/pipelines/runs/{runId}/coverage/assemblies", query),
            JsonOptions.Web, ct) ?? new();
    }

    public async Task<PackageFeedDetailDto?> GetPackageFeedAsync(int id)
    {
        var response = await http.GetAsync($"api/package-feeds/{id}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PackageFeedDetailDto>(JsonOptions.Web);
    }

    public Task<PackageFeedDto?> CreatePackageFeedAsync(CreatePackageFeedRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreatePackageFeedRequest, PackageFeedDto>("api/package-feeds", request, ct);

    public Task<ApiStatus> DeletePackageFeedAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/package-feeds/{id}", ct);

    public Task<PackageEntryDto?> AddPackageToFeedAsync(int feedId, AddPackageRequest request, CancellationToken ct = default)
        => PostJsonAsync<AddPackageRequest, PackageEntryDto>($"api/package-feeds/{feedId}/packages", request, ct);

    public Task<ApiStatus> RemovePackageFromFeedAsync(int feedId, int packageId, CancellationToken ct = default)
        => DeleteAsync($"api/package-feeds/{feedId}/packages/{packageId}", ct);

    public async Task<PackageFeedSyncResultDto?> SyncPackageFeedAsync(int feedId)
    {
        var response = await http.PostAsync($"api/package-feeds/{feedId}/sync", null);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<PackageFeedSyncResultDto>(JsonOptions.Web);
    }

    public async Task<PipelineLintSummaryDto?> GetLintSummaryAsync(int runId, CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<PipelineLintSummaryDto>($"api/pipelines/runs/{runId}/lint", JsonOptions.Web, ct); }
        catch (HttpRequestException) { return null; }
    }

    public Task<PipelineYamlDefinition?> ValidatePipelineYamlAsync(string yaml, CancellationToken ct = default)
        => PostJsonAsync<ValidateYamlRequest, PipelineYamlDefinition>("api/pipelines/validate", new ValidateYamlRequest { Yaml = yaml }, ct);

    // --- Projects ---
    public async Task<PaginatedResult<ProjectDto>> GetProjectsAsync(int page = 1, int pageSize = 25, string? search = null, ProjectStatus? status = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrEmpty(search)) query["search"] = search;
        if (status.HasValue) query["projectStatus"] = status.Value.ToString();
        return await http.GetFromJsonAsync<PaginatedResult<ProjectDto>>(
            QueryHelpers.AddQueryString("api/projects", query), JsonOptions.Web) ?? new();
    }

    public async Task<ProjectDetailDto?> GetProjectDetailAsync(int id, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<ProjectDetailDto>($"api/projects/{id}", JsonOptions.Web, ct);
    }

    public async Task<List<ProjectDto>> GetAllProjectsAsync()
    {
        const int pageSize = 100;
        var items = new List<ProjectDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetProjectsAsync(page, pageSize);
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

    // --- Releases ---
    public async Task<PaginatedResult<ReleaseDto>> GetReleasesAsync(int page = 1, int pageSize = 25, int? projectId = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (projectId.HasValue) query["projectId"] = projectId.Value.ToString();
        return await http.GetFromJsonAsync<PaginatedResult<ReleaseDto>>(
            QueryHelpers.AddQueryString("api/releases", query), JsonOptions.Web) ?? new();
    }

    public async Task<ReleaseDto?> GetReleaseAsync(int id, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<ReleaseDto>($"api/releases/{id}", JsonOptions.Web, ct);
    }

    public async Task<List<ReleaseDto>> GetReleasesByRunAsync(int runId, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<ReleaseDto>>($"api/releases/by-run/{runId}", JsonOptions.Web, ct) ?? [];
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
        => await http.GetFromJsonAsync<ReleaseRollbackPreviewDto>($"api/releases/{id}/rollback-preview", JsonOptions.Web, ct);

    public Task<ReleaseRollbackDto?> RollbackReleaseAsync(int id, RollbackReleaseRequest request, CancellationToken ct = default)
        => PostJsonAsync<RollbackReleaseRequest, ReleaseRollbackDto>($"api/releases/{id}/rollback", request, ct);

    public async Task<ReleaseDto?> PromoteReleaseAsync(int id, CancellationToken ct = default)
        => await PostNoBodyAsync<ReleaseDto>($"api/releases/{id}/promote", ct);
    // --- PortSentry ---
    public async Task<PortsentryDataDto> GetPortsentryStateAsync(int serverId, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<PortsentryDataDto>($"api/servers/{serverId}/portsentry", JsonOptions.Web, ct) ?? new();
    }

    public Task<ApiStatus> ExecutePortsentryActionAsync(int serverId, PortsentryActionRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<PortsentryActionRequest>($"api/servers/{serverId}/portsentry/action", request, ct);

    public Task<ApiStatus> SetupPortsentryAsync(int serverId, PortsentrySetupRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<PortsentrySetupRequest>($"api/servers/{serverId}/portsentry/setup", request, ct);

    public Task<ApiStatus> GetPortsentryLogsAsync(int serverId, PortsentryLogRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<PortsentryLogRequest>($"api/servers/{serverId}/portsentry/logs", request, ct);

    public Task<ApiStatus> UnblockPortsentryIpAsync(int serverId, PortsentryUnblockRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<PortsentryUnblockRequest>($"api/servers/{serverId}/portsentry/unblock", request, ct);

    public async Task<ApiStatus> GetPortsentryStatusAsync(int serverId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"api/servers/{serverId}/portsentry/status", ct);
        return ApiStatus.From(response);
    }

    public async Task<PaginatedResult<PortsentryBlockedIpDto>> GetPortsentryBlockedIpsAsync(
        int serverId, int page, int pageSize, string? search = null, string? sortBy = null, bool sortDescending = false)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await http.GetFromJsonAsync<PaginatedResult<PortsentryBlockedIpDto>>(
            QueryHelpers.AddQueryString($"api/servers/{serverId}/portsentry/blocked", query), JsonOptions.Web) ?? new();
    }

    public async Task<PaginatedResult<PortsentryWhitelistIpDto>> GetPortsentryWhitelistPageAsync(
        int serverId, int page, int pageSize, string? search = null, string? sortBy = null, bool sortDescending = false)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await http.GetFromJsonAsync<PaginatedResult<PortsentryWhitelistIpDto>>(
            QueryHelpers.AddQueryString($"api/servers/{serverId}/portsentry/whitelist", query), JsonOptions.Web) ?? new();
    }

    public async Task<List<PortsentryWhitelistIpDto>> GetPortsentryWhitelistAsync(int serverId)
    {
        const int pageSize = 100;
        var items = new List<PortsentryWhitelistIpDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetPortsentryWhitelistPageAsync(serverId, page, pageSize);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public Task<PortsentryWhitelistIpDto?> AddPortsentryWhitelistIpAsync(int serverId, AddPortsentryWhitelistRequest request, CancellationToken ct = default)
        => PostJsonAsync<AddPortsentryWhitelistRequest, PortsentryWhitelistIpDto>($"api/servers/{serverId}/portsentry/whitelist", request, ct);

    public Task<ApiStatus> RemovePortsentryWhitelistIpAsync(int serverId, int id, CancellationToken ct = default)
        => DeleteAsync($"api/servers/{serverId}/portsentry/whitelist/{id}", ct);
    // --- RKHunter ---
    public async Task<RkhunterDataDto> GetRkhunterStateAsync(int serverId, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<RkhunterDataDto>($"api/servers/{serverId}/rkhunter", JsonOptions.Web, ct) ?? new();
    }

    public Task<ApiStatus> ExecuteRkhunterActionAsync(int serverId, RkhunterActionRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<RkhunterActionRequest>($"api/servers/{serverId}/rkhunter/action", request, ct);

    public Task<ApiStatus> SetupRkhunterAsync(int serverId, RkhunterSetupRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<RkhunterSetupRequest>($"api/servers/{serverId}/rkhunter/setup", request, ct);

    public Task<ApiStatus> GetRkhunterLogsAsync(int serverId, RkhunterLogRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<RkhunterLogRequest>($"api/servers/{serverId}/rkhunter/logs", request, ct);

    public async Task<List<RkhunterWarningDto>> GetRkhunterWarningsAsync(int serverId, bool includeArchived = false)
    {
        return await http.GetFromJsonAsync<List<RkhunterWarningDto>>(
            $"api/servers/{serverId}/rkhunter/warnings?includeArchived={includeArchived}", JsonOptions.Web) ?? [];
    }

    public async Task<List<RkhunterScanResultDto>> GetRkhunterScanHistoryAsync(int serverId, int limit = 50)
    {
        return await http.GetFromJsonAsync<List<RkhunterScanResultDto>>(
            $"api/servers/{serverId}/rkhunter/history?limit={limit}", JsonOptions.Web) ?? [];
    }

    public Task<ApiStatus> SetRkhunterScheduleAsync(int serverId, RkhunterScheduleRequest request, CancellationToken ct = default)
        => PutJsonNoBodyAsync<RkhunterScheduleRequest>($"api/servers/{serverId}/rkhunter/schedule", request, ct);
    // --- Roles ---

    public async Task<PaginatedResult<RoleDto>> GetRoleDtosAsync(
        int page, int pageSize, string? search = null, string? sortBy = null, bool sortDescending = false)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await http.GetFromJsonAsync<PaginatedResult<RoleDto>>(
            QueryHelpers.AddQueryString("api/roles", query), JsonOptions.Web) ?? new();
    }

    public async Task<RoleDto?> GetRoleAsync(int id, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<RoleDto>($"api/roles/{id}", JsonOptions.Web, ct);
    }

    public Task<RoleDto?> CreateRoleAsync(CreateRoleRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateRoleRequest, RoleDto>("api/roles", request, ct);

    public Task<RoleDto?> UpdateRoleAsync(int id, UpdateRoleRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateRoleRequest, RoleDto>($"api/roles/{id}", request, ct);

    public Task<ApiStatus> DeleteRoleAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/roles/{id}", ct);

    public async Task<List<ResourcePermissionDto>> GetRolePermissionsAsync(int roleId)
    {
        return await http.GetFromJsonAsync<List<ResourcePermissionDto>>($"api/roles/{roleId}/permissions", JsonOptions.Web) ?? [];
    }

    public Task<ApiStatus> SetRolePermissionsAsync(int roleId, SetResourcePermissionsRequest request, CancellationToken ct = default)
        => PutJsonNoBodyAsync<SetResourcePermissionsRequest>($"api/roles/{roleId}/permissions", request, ct);

    public Task<RoleDto?> CloneRoleAsync(int roleId, CancellationToken ct = default)
        => PostNoBodyAsync<RoleDto>($"api/roles/{roleId}/clone", ct);

    public async Task<PaginatedResult<RoleUserDto>> GetRoleUsersAsync(
        int roleId, int page, int pageSize, string? search = null, string? sortBy = null,
        bool sortDescending = false, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await http.GetFromJsonAsync<PaginatedResult<RoleUserDto>>(
            QueryHelpers.AddQueryString($"api/roles/{roleId}/users", query), JsonOptions.Web, ct) ?? new();
    }

    public async Task<PaginatedResult<RoleUserDto>> GetUsersAvailableForRoleAsync(
        int roleId, int page, int pageSize, string? search = null, string? sortBy = null,
        bool sortDescending = false, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await http.GetFromJsonAsync<PaginatedResult<RoleUserDto>>(
            QueryHelpers.AddQueryString($"api/roles/{roleId}/available-users", query), JsonOptions.Web, ct) ?? new();
    }

    public Task<ApiStatus> AddUserToRoleAsync(int roleId, int userId, CancellationToken ct = default)
        => PostJsonNoBodyAsync<AddUserToRoleRequest>($"api/roles/{roleId}/users", new AddUserToRoleRequest { UserId = userId }, ct);

    public Task<ApiStatus> RemoveUserFromRoleAsync(int roleId, int userId, CancellationToken ct = default)
        => DeleteAsync($"api/roles/{roleId}/users/{userId}", ct);

    public async Task<UserPermissionSummaryDto?> GetUserEffectivePermissionsAsync(int userId, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<UserPermissionSummaryDto>($"api/users/{userId}/effective-permissions", JsonOptions.Web, ct);
    }

    public async Task<UserPermissionSummaryDto?> GetMyPermissionsAsync(CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<UserPermissionSummaryDto>("api/users/me/permissions", JsonOptions.Web, ct);
    }
    // Return the enqueued task id (null on failure) so the caller can correlate the resulting
    // TaskCompleted notification by id instead of by task-name suffix (which collides across
    // concurrent operations on the same service).
    public async Task<int?> ExecuteServiceActionAsync(int serverId, ServiceActionRequest request, CancellationToken ct = default)
        => (await PostJsonAsync<ServiceActionRequest, ServiceTaskResponse>($"api/servers/{serverId}/services/action", request, ct).ConfigureAwait(false))?.TaskId;

    public async Task<int?> InstallServiceAsync(int serverId, string serviceName, CancellationToken ct = default)
        => (await PostJsonAsync<ServiceInstallRequest, ServiceTaskResponse>($"api/servers/{serverId}/services/install", new ServiceInstallRequest { ServiceName = serviceName }, ct).ConfigureAwait(false))?.TaskId;

    public async Task<int?> UninstallServiceAsync(int serverId, string serviceName, CancellationToken ct = default)
        => (await PostJsonAsync<ServiceInstallRequest, ServiceTaskResponse>($"api/servers/{serverId}/services/uninstall", new ServiceInstallRequest { ServiceName = serviceName }, ct).ConfigureAwait(false))?.TaskId;

    // --- Fleet patching (PLAN-006 4.1) ---
    public Task<ServerSecurityUpdatesDto?> GetSecurityUpdatesAsync(int serverId, CancellationToken ct = default)
        => GetJsonAsync<ServerSecurityUpdatesDto>($"api/servers/{serverId}/security-updates", ct);

    public async Task<int?> UpgradeSystemAsync(int serverId, bool dryRun, CancellationToken ct = default)
        => (await PostJsonAsync<SystemUpgradeRequest, ServiceTaskResponse>($"api/servers/{serverId}/system/upgrade", new SystemUpgradeRequest { DryRun = dryRun }, ct).ConfigureAwait(false))?.TaskId;

    // --- App backups (PLAN-006 4.3) ---
    public async Task<PaginatedResult<BackupPolicyDto>> GetBackupPoliciesAsync(
        int page = 1, int pageSize = 25, string? search = null,
        string? sortBy = null, bool sortDescending = false, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString(),
            ["search"] = search,
            ["sortBy"] = sortBy,
            ["sortDescending"] = sortDescending.ToString()
        };
        return await GetJsonAsync<PaginatedResult<BackupPolicyDto>>(
            QueryHelpers.AddQueryString("api/backups", query), ct).ConfigureAwait(false) ?? new();
    }

    public async Task<List<BackupPolicyDto>> GetAllBackupPoliciesAsync(CancellationToken ct = default)
    {
        const int pageSize = 100;
        var items = new List<BackupPolicyDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetBackupPoliciesAsync(page, pageSize, ct: ct).ConfigureAwait(false);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public Task<BackupPolicyDto?> CreateBackupPolicyAsync(CreateBackupPolicyRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateBackupPolicyRequest, BackupPolicyDto>("api/backups", request, ct);

    public Task<BackupPolicyDto?> UpdateBackupPolicyAsync(int id, UpdateBackupPolicyRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateBackupPolicyRequest, BackupPolicyDto>($"api/backups/{id}", request, ct);

    public Task<ApiStatus> DeleteBackupPolicyAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/backups/{id}", ct);

    public async Task<PaginatedResult<BackupRunDto>> GetBackupRunsAsync(
        int policyId, int page = 1, int pageSize = 25, string? search = null,
        string? sortBy = null, bool sortDescending = false, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString(),
            ["search"] = search,
            ["sortBy"] = sortBy,
            ["sortDescending"] = sortDescending.ToString()
        };
        return await GetJsonAsync<PaginatedResult<BackupRunDto>>(
            QueryHelpers.AddQueryString($"api/backups/{policyId}/runs", query), ct).ConfigureAwait(false) ?? new();
    }

    public async Task<List<BackupRunDto>> GetAllBackupRunsAsync(
        int policyId, CancellationToken ct = default)
    {
        const int pageSize = 100;
        var items = new List<BackupRunDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetBackupRunsAsync(policyId, page, pageSize, ct: ct).ConfigureAwait(false);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public Task<ApiStatus> RunBackupNowAsync(int policyId, CancellationToken ct = default)
        => PostNoBodyAsync($"api/backups/{policyId}/run", ct);

    // --- Firewall (PLAN-006 4.2) ---
    public Task<ServerFirewallDto?> GetFirewallAsync(int serverId, CancellationToken ct = default)
        => GetJsonAsync<ServerFirewallDto>($"api/servers/{serverId}/firewall", ct);

    public async Task<int?> FirewallAllowAsync(int serverId, FirewallRuleRequest request, CancellationToken ct = default)
        => (await PostJsonAsync<FirewallRuleRequest, ServiceTaskResponse>($"api/servers/{serverId}/firewall/allow", request, ct).ConfigureAwait(false))?.TaskId;

    public async Task<int?> FirewallDenyAsync(int serverId, FirewallRuleRequest request, CancellationToken ct = default)
        => (await PostJsonAsync<FirewallRuleRequest, ServiceTaskResponse>($"api/servers/{serverId}/firewall/deny", request, ct).ConfigureAwait(false))?.TaskId;

    public async Task<int?> FirewallDeleteRuleAsync(int serverId, FirewallRuleRequest request, CancellationToken ct = default)
        => (await PostJsonAsync<FirewallRuleRequest, ServiceTaskResponse>($"api/servers/{serverId}/firewall/delete", request, ct).ConfigureAwait(false))?.TaskId;

    public async Task<int?> FirewallToggleAsync(int serverId, bool enabled, CancellationToken ct = default)
        => (await PostJsonAsync<FirewallToggleRequest, ServiceTaskResponse>($"api/servers/{serverId}/firewall/toggle", new FirewallToggleRequest { Enabled = enabled }, ct).ConfigureAwait(false))?.TaskId;

    public async Task<int?> RequestServiceLogsAsync(int serverId, string serviceName, int lines = 100, bool follow = false, CancellationToken ct = default)
    {
        var result = await PostJsonAsync<ServiceLogsRequest, ServiceLogsResponse>(
            $"api/servers/{serverId}/services/logs",
            new ServiceLogsRequest { ServiceName = serviceName, Lines = lines, Follow = follow }, ct).ConfigureAwait(false);
        return result?.TaskId;
    }

    public async Task<string?> GetAgentServerUrlAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await http.GetFromJsonAsync<AgentServerUrlResponse>("api/servers/agent-server-url", JsonOptions.Web, ct);
            return result?.Url;
        }
        catch (HttpRequestException) { return null; }
    }

    private sealed record AgentServerUrlResponse(string Url);
    private static Dictionary<string, string?> BuildSystemLogFilters(string? fileName, string? level, string? search, DateTime? dateFrom, DateTime? dateTo)
    {
        var query = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(fileName)) query["fileName"] = fileName;
        if (!string.IsNullOrWhiteSpace(level)) query["level"] = level;
        if (!string.IsNullOrWhiteSpace(search)) query["search"] = search;
        if (dateFrom.HasValue) query["dateFrom"] = dateFrom.Value.ToString("O");
        if (dateTo.HasValue) query["dateTo"] = dateTo.Value.ToString("O");
        return query;
    }

    public async Task<List<SystemLogFileDto>> GetSystemLogFilesAsync()
    {
        return await http.GetFromJsonAsync<List<SystemLogFileDto>>("api/system-logs/files", JsonOptions.Web) ?? [];
    }

    public async Task<PaginatedResult<SystemLogEntryDto>> GetSystemLogEntriesAsync(
        string? fileName, string? level, string? search, DateTime? dateFrom, DateTime? dateTo,
        int page = 1, int pageSize = 50)
    {
        var query = BuildSystemLogFilters(fileName, level, search, dateFrom, dateTo);
        query["page"] = page.ToString();
        query["pageSize"] = pageSize.ToString();
        return await http.GetFromJsonAsync<PaginatedResult<SystemLogEntryDto>>(
            QueryHelpers.AddQueryString("api/system-logs/entries", query), JsonOptions.Web) ?? new();
    }

    public async Task<byte[]?> DownloadSystemLogFileAsync(string fileName, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"api/system-logs/download/{Uri.EscapeDataString(fileName)}", ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<byte[]?> ExportSystemLogsCsvAsync(
        string? fileName, string? level, string? search, DateTime? dateFrom, DateTime? dateTo, CancellationToken ct = default)
    {
        var query = BuildSystemLogFilters(fileName, level, search, dateFrom, dateTo);
        var response = await http.GetAsync(QueryHelpers.AddQueryString("api/system-logs/export", query), ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<int?> PurgeSystemLogsAsync(int retentionDays = 30, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"api/system-logs/purge?retentionDays={retentionDays}", ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<int>(JsonOptions.Web, ct).ConfigureAwait(false)
            : null;
    }
    // --- TeamSpeak ---
    public async Task<TeamspeakDataDto> GetTeamspeakStateAsync(int serverId, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<TeamspeakDataDto>($"api/servers/{serverId}/teamspeak", JsonOptions.Web, ct) ?? new();
    }

    public Task<PaginatedResult<TeamspeakClientDto>> GetTeamspeakClientsAsync(
        int serverId, int page, int pageSize, string? search = null, string? sortBy = null,
        bool sortDescending = false, CancellationToken ct = default) =>
        GetTeamspeakPageAsync<TeamspeakClientDto>(
            $"api/servers/{serverId}/teamspeak/clients", page, pageSize, search, sortBy, sortDescending, ct);

    public Task<PaginatedResult<TeamspeakChannelDto>> GetTeamspeakChannelsAsync(
        int serverId, int page, int pageSize, string? search = null, string? sortBy = null,
        bool sortDescending = false, CancellationToken ct = default) =>
        GetTeamspeakPageAsync<TeamspeakChannelDto>(
            $"api/servers/{serverId}/teamspeak/channels", page, pageSize, search, sortBy, sortDescending, ct);

    public Task<PaginatedResult<TeamspeakBanDto>> GetTeamspeakBansAsync(
        int serverId, int page, int pageSize, string? search = null, string? sortBy = null,
        bool sortDescending = false, CancellationToken ct = default) =>
        GetTeamspeakPageAsync<TeamspeakBanDto>(
            $"api/servers/{serverId}/teamspeak/bans", page, pageSize, search, sortBy, sortDescending, ct);

    private async Task<PaginatedResult<T>> GetTeamspeakPageAsync<T>(
        string path, int page, int pageSize, string? search, string? sortBy,
        bool sortDescending, CancellationToken ct)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await http.GetFromJsonAsync<PaginatedResult<T>>(
            QueryHelpers.AddQueryString(path, query), JsonOptions.Web, ct).ConfigureAwait(false) ?? new();
    }

    public Task<ApiStatus> ExecuteTeamspeakActionAsync(int serverId, TeamspeakActionRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<TeamspeakActionRequest>($"api/servers/{serverId}/teamspeak/action", request, ct);

    public Task<ApiStatus> SetupTeamspeakAsync(int serverId, TeamspeakSetupRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<TeamspeakSetupRequest>($"api/servers/{serverId}/teamspeak/setup", request, ct);

    public Task<ApiStatus> GetTeamspeakLogsAsync(int serverId, TeamspeakLogRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<TeamspeakLogRequest>($"api/servers/{serverId}/teamspeak/logs", request, ct);

    public Task<ApiStatus> KickTeamspeakClientAsync(int serverId, TeamspeakKickRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<TeamspeakKickRequest>($"api/servers/{serverId}/teamspeak/kick", request, ct);

    public Task<ApiStatus> BanTeamspeakClientAsync(int serverId, TeamspeakBanRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<TeamspeakBanRequest>($"api/servers/{serverId}/teamspeak/ban", request, ct);

    // Item #9 tier-1
    public Task<ApiStatus> MoveTeamspeakClientAsync(int serverId, TeamspeakMoveClientRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<TeamspeakMoveClientRequest>($"api/servers/{serverId}/teamspeak/move-client", request, ct);

    public Task<ApiStatus> PokeTeamspeakClientAsync(int serverId, TeamspeakPokeClientRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<TeamspeakPokeClientRequest>($"api/servers/{serverId}/teamspeak/poke", request, ct);

    public Task<ApiStatus> UnbanTeamspeakClientAsync(int serverId, int banId, CancellationToken ct = default)
        => DeleteAsync($"api/servers/{serverId}/teamspeak/bans/{banId}", ct);

    public Task<ApiStatus> CreateTeamspeakChannelAsync(int serverId, TeamspeakCreateChannelRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<TeamspeakCreateChannelRequest>($"api/servers/{serverId}/teamspeak/channels", request, ct);

    public Task<ApiStatus> EditTeamspeakChannelAsync(int serverId, int channelId, TeamspeakEditChannelRequest request, CancellationToken ct = default)
        => PutJsonNoBodyAsync<TeamspeakEditChannelRequest>($"api/servers/{serverId}/teamspeak/channels/{channelId}", request, ct);

    public Task<ApiStatus> DeleteTeamspeakChannelAsync(int serverId, int channelId, CancellationToken ct = default)
        => DeleteAsync($"api/servers/{serverId}/teamspeak/channels/{channelId}", ct);

    public Task<ApiStatus> EditTeamspeakServerAsync(int serverId, TeamspeakServerEditRequest request, CancellationToken ct = default)
        => PutJsonNoBodyAsync<TeamspeakServerEditRequest>($"api/servers/{serverId}/teamspeak/server", request, ct);

    public Task<ApiStatus> SendTeamspeakGlobalMessageAsync(int serverId, TeamspeakGlobalMessageRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<TeamspeakGlobalMessageRequest>($"api/servers/{serverId}/teamspeak/message", request, ct);

    // ===== Item #9 tier-2/3 =====

    public Task<ApiStatus> GetTeamspeakClientInfoAsync(int serverId, TeamspeakClientInfoRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<TeamspeakClientInfoRequest>($"api/servers/{serverId}/teamspeak/clientinfo", request, ct);

    public Task<ApiStatus> TeamspeakGracefulRestartAsync(int serverId, TeamspeakGracefulRestartRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<TeamspeakGracefulRestartRequest>($"api/servers/{serverId}/teamspeak/graceful-restart", request, ct);

    public Task<ApiStatus> CreateTeamspeakSnapshotAsync(int serverId, TeamspeakSnapshotCreateRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<TeamspeakSnapshotCreateRequest>($"api/servers/{serverId}/teamspeak/snapshots", request, ct);

    public Task<ApiStatus> DeployTeamspeakSnapshotAsync(int serverId, TeamspeakSnapshotDeployRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<TeamspeakSnapshotDeployRequest>($"api/servers/{serverId}/teamspeak/snapshots/deploy", request, ct);

    public async Task<ApiStatus> ListTeamspeakServerGroupsAsync(int serverId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"api/servers/{serverId}/teamspeak/server-groups", ct);
        return ApiStatus.From(response);
    }

    public Task<ApiStatus> AddTeamspeakServerGroupClientAsync(int serverId, TeamspeakServerGroupAddRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<TeamspeakServerGroupAddRequest>($"api/servers/{serverId}/teamspeak/server-groups/add", request, ct);

    public Task<ApiStatus> RemoveTeamspeakServerGroupClientAsync(int serverId, TeamspeakServerGroupRemoveRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<TeamspeakServerGroupRemoveRequest>($"api/servers/{serverId}/teamspeak/server-groups/remove", request, ct);

    public async Task<ApiStatus> ListTeamspeakTokensAsync(int serverId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"api/servers/{serverId}/teamspeak/tokens", ct);
        return ApiStatus.From(response);
    }

    public Task<ApiStatus> CreateTeamspeakTokenAsync(int serverId, TeamspeakTokenCreateRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<TeamspeakTokenCreateRequest>($"api/servers/{serverId}/teamspeak/tokens", request, ct);

    public async Task<ApiStatus> DeleteTeamspeakTokenAsync(int serverId, TeamspeakTokenDeleteRequest request, CancellationToken ct = default)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Delete, $"api/servers/{serverId}/teamspeak/tokens")
        {
            Content = JsonContent.Create(request, options: JsonOptions.Web)
        };
        var response = await http.SendAsync(msg, ct);
        return ApiStatus.From(response);
    }

    public async Task<ApiStatus> GetTeamspeakServerInfoAsync(int serverId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"api/servers/{serverId}/teamspeak/server-info", ct);
        return ApiStatus.From(response);
    }

    public async Task<ApiStatus> ListTeamspeakComplaintsAsync(int serverId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"api/servers/{serverId}/teamspeak/complaints", ct);
        return ApiStatus.From(response);
    }

    public async Task<ApiStatus> DeleteTeamspeakComplaintAsync(int serverId, TeamspeakComplaintDeleteRequest request, CancellationToken ct = default)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Delete, $"api/servers/{serverId}/teamspeak/complaints")
        {
            Content = JsonContent.Create(request, options: JsonOptions.Web)
        };
        var response = await http.SendAsync(msg, ct);
        return ApiStatus.From(response);
    }
    public async Task<TotpSetupResponse?> SetupTotpAsync(CancellationToken ct = default)
        => await PostNoBodyAsync<TotpSetupResponse>("api/auth/totp/setup", ct).ConfigureAwait(false);

    public async Task<bool> VerifyTotpAsync(string code, CancellationToken ct = default)
    {
        var status = await PostJsonNoBodyAsync("api/auth/totp/verify", new TotpVerifyRequest { Code = code }, ct).ConfigureAwait(false);
        return status.Success;
    }

    public async Task<bool> DisableTotpAsync(string password, CancellationToken ct = default)
    {
        var status = await PostJsonNoBodyAsync("api/auth/totp/disable", new TotpDisableRequest { Password = password }, ct).ConfigureAwait(false);
        return status.Success;
    }
    // --- Variable Libraries ---
    public async Task<PaginatedResult<VariableLibraryDto>> GetVariableLibrariesAsync(int page = 1, int pageSize = 25, string? search = null, int? projectId = null, int? environmentId = null, int? projectServerId = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrEmpty(search)) query["search"] = search;
        if (projectId.HasValue) query["projectId"] = projectId.Value.ToString();
        if (environmentId.HasValue) query["environmentId"] = environmentId.Value.ToString();
        if (projectServerId.HasValue) query["projectServerId"] = projectServerId.Value.ToString();
        return await http.GetFromJsonAsync<PaginatedResult<VariableLibraryDto>>(
            QueryHelpers.AddQueryString("api/variable-libraries", query), JsonOptions.Web) ?? new();
    }

    public async Task<VariableLibraryDetailDto?> GetVariableLibraryDetailAsync(int id, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<VariableLibraryDetailDto>($"api/variable-libraries/{id}", JsonOptions.Web, ct);
    }

    public async Task<List<VariableLibraryDto>> GetAllVariableLibrariesAsync(int? projectId = null)
    {
        const int pageSize = 100;
        var items = new List<VariableLibraryDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetVariableLibrariesAsync(page, pageSize, projectId: projectId);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }

    public async Task<List<string>> GetVariableLibraryNamesAsync(int? projectId = null)
    {
        var query = new Dictionary<string, string?>();
        if (projectId.HasValue) query["projectId"] = projectId.Value.ToString();
        return await http.GetFromJsonAsync<List<string>>(
            QueryHelpers.AddQueryString("api/variable-libraries/names", query), JsonOptions.Web) ?? [];
    }

    public async Task<PaginatedResult<string>> GetVariableSuggestionKeysAsync(
        int page, int pageSize, int? projectId = null, string? search = null,
        CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, "Key", false);
        if (projectId.HasValue) query["projectId"] = projectId.Value.ToString();
        return await http.GetFromJsonAsync<PaginatedResult<string>>(
            QueryHelpers.AddQueryString("api/variable-libraries/suggestion-keys", query),
            JsonOptions.Web, ct) ?? new();
    }

    public Task<VariableLibraryDto?> CreateVariableLibraryAsync(CreateVariableLibraryRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateVariableLibraryRequest, VariableLibraryDto>("api/variable-libraries", request, ct);

    public Task<VariableLibraryDto?> UpdateVariableLibraryAsync(int id, UpdateVariableLibraryRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateVariableLibraryRequest, VariableLibraryDto>($"api/variable-libraries/{id}", request, ct);

    public Task<ApiStatus> DeleteVariableLibraryAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/variable-libraries/{id}", ct);

    public Task<VariableEntryDto?> CreateVariableEntryAsync(int libraryId, CreateVariableEntryRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateVariableEntryRequest, VariableEntryDto>($"api/variable-libraries/{libraryId}/entries", request, ct);

    public Task<VariableEntryDto?> UpdateVariableEntryAsync(int libraryId, int entryId, UpdateVariableEntryRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateVariableEntryRequest, VariableEntryDto>($"api/variable-libraries/{libraryId}/entries/{entryId}", request, ct);

    public Task<ApiStatus> DeleteVariableEntryAsync(int libraryId, int entryId, CancellationToken ct = default)
        => DeleteAsync($"api/variable-libraries/{libraryId}/entries/{entryId}", ct);

    public async Task<PaginatedResult<VariableEntryDto>> GetVariableEntriesPageAsync(
        int libraryId, int page, int pageSize, string? search = null, string? sortBy = null,
        bool sortDescending = false, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await http.GetFromJsonAsync<PaginatedResult<VariableEntryDto>>(
            QueryHelpers.AddQueryString($"api/variable-libraries/{libraryId}/entries", query), JsonOptions.Web, ct) ?? new();
    }

    public async Task<PaginatedResult<VariableEntryVersionDto>> GetVariableEntryVersionsPageAsync(
        int libraryId, int entryId, int page, int pageSize, string? search = null,
        string? sortBy = null, bool sortDescending = false, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await http.GetFromJsonAsync<PaginatedResult<VariableEntryVersionDto>>(
            QueryHelpers.AddQueryString(
                $"api/variable-libraries/{libraryId}/entries/{entryId}/versions", query), JsonOptions.Web, ct) ?? new();
    }

    public async Task<List<VariableEntryDto>> ExportVariableEntriesAsync(int libraryId)
    {
        return await http.GetFromJsonAsync<List<VariableEntryDto>>($"api/variable-libraries/{libraryId}/export", JsonOptions.Web) ?? [];
    }

    public Task<ImportResultDto?> ImportVariableEntriesAsync(int libraryId, List<CreateVariableEntryRequest> entries, CancellationToken ct = default)
        => PostJsonAsync<List<CreateVariableEntryRequest>, ImportResultDto>($"api/variable-libraries/{libraryId}/import", entries, ct);

    // --- Vaults ---
    public async Task<PaginatedResult<VaultDto>> GetVaultsAsync(int page = 1, int pageSize = 25, string? search = null, int? projectId = null, int? environmentId = null, int? projectServerId = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrEmpty(search)) query["search"] = search;
        if (projectId.HasValue) query["projectId"] = projectId.Value.ToString();
        if (environmentId.HasValue) query["environmentId"] = environmentId.Value.ToString();
        if (projectServerId.HasValue) query["projectServerId"] = projectServerId.Value.ToString();
        return await http.GetFromJsonAsync<PaginatedResult<VaultDto>>(
            QueryHelpers.AddQueryString("api/vaults", query), JsonOptions.Web) ?? new();
    }

    public async Task<VaultDetailDto?> GetVaultDetailAsync(int id, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<VaultDetailDto>($"api/vaults/{id}", JsonOptions.Web, ct);
    }

    public async Task<List<string>> GetVaultNamesAsync(int? projectId = null)
    {
        var query = new Dictionary<string, string?>();
        if (projectId.HasValue) query["projectId"] = projectId.Value.ToString();
        return await http.GetFromJsonAsync<List<string>>(
            QueryHelpers.AddQueryString("api/vaults/names", query), JsonOptions.Web) ?? [];
    }

    public Task<VaultDto?> CreateVaultAsync(CreateVaultRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateVaultRequest, VaultDto>("api/vaults", request, ct);

    public Task<VaultDto?> UpdateVaultAsync(int id, UpdateVaultRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateVaultRequest, VaultDto>($"api/vaults/{id}", request, ct);

    public Task<ApiStatus> DeleteVaultAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/vaults/{id}", ct);

    public Task<VaultSecretDto?> CreateVaultSecretAsync(int vaultId, CreateVaultSecretRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateVaultSecretRequest, VaultSecretDto>($"api/vaults/{vaultId}/secrets", request, ct);

    public Task<VaultSecretDto?> UpdateVaultSecretAsync(int vaultId, int secretId, UpdateVaultSecretRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateVaultSecretRequest, VaultSecretDto>($"api/vaults/{vaultId}/secrets/{secretId}", request, ct);

    public Task<VaultSecretDto?> RotateVaultSecretAsync(int vaultId, int secretId, RotateVaultSecretRequest request, CancellationToken ct = default)
        => PostJsonAsync<RotateVaultSecretRequest, VaultSecretDto>($"api/vaults/{vaultId}/secrets/{secretId}/rotate", request, ct);

    public Task<ApiStatus> DeleteVaultSecretAsync(int vaultId, int secretId, CancellationToken ct = default)
        => DeleteAsync($"api/vaults/{vaultId}/secrets/{secretId}", ct);

    public async Task<List<VaultSecretVersionDto>> GetVaultSecretVersionsAsync(int vaultId, int secretId)
    {
        return await http.GetFromJsonAsync<List<VaultSecretVersionDto>>($"api/vaults/{vaultId}/secrets/{secretId}/versions", JsonOptions.Web) ?? [];
    }

    public async Task<List<string>> ExportVaultSecretKeysAsync(int vaultId)
    {
        return await http.GetFromJsonAsync<List<string>>($"api/vaults/{vaultId}/export-keys", JsonOptions.Web) ?? [];
    }

    public Task<ImportResultDto?> ImportVaultSecretsAsync(int vaultId, List<CreateVaultSecretRequest> secrets, CancellationToken ct = default)
        => PostJsonAsync<List<CreateVaultSecretRequest>, ImportResultDto>($"api/vaults/{vaultId}/import", secrets, ct);

    private static Dictionary<string, string?> BuildPaginationQuery(
        int page, int pageSize, string? search, string? sortBy, bool sortDescending) => new()
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString(),
            ["search"] = search,
            ["sortBy"] = sortBy,
            ["sortDescending"] = sortDescending.ToString().ToLowerInvariant()
        };
}
