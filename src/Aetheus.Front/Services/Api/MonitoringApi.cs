// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Aetheus.Shared.DTOs.Organizations;
using Microsoft.AspNetCore.WebUtilities;

namespace Aetheus.Front.Services.Api;

/// <summary>Everything observed rather than commanded: server and application monitoring, OTLP telemetry, system logs, the audit trail, dashboards, alerts and notifications.</summary>
public sealed class MonitoringApi(HttpClient http) : ApiClientBase(http)
{
    public async Task<DashboardOverviewDto?> GetDashboardAsync()
    {
        return await Http.GetFromJsonAsync<DashboardOverviewDto>("api/monitoring/dashboard", JsonOptions.Web);
    }


    public async Task<List<ServerMetricDto>> GetServerMetricsAsync(
        int serverId,
        int hours = 24,
        CancellationToken ct = default,
        DateTime? afterUtc = null,
        int take = 1_000)
    {
        var after = afterUtc.HasValue
            ? $"&afterUtc={Uri.EscapeDataString(afterUtc.Value.ToUniversalTime().ToString("O"))}"
            : string.Empty;
        return await Http.GetFromJsonAsync<List<ServerMetricDto>>(
            $"api/monitoring/servers/{serverId}/metrics?hours={hours}&take={Math.Clamp(take, 1, 2_000)}{after}",
            JsonOptions.Web,
            ct) ?? [];
    }

    public async Task<List<MonitoredAppDto>> GetMonitoredAppsAsync(int projectId, CancellationToken ct = default)
    {
        return await Http.GetFromJsonAsync<List<MonitoredAppDto>>($"api/appmonitoring/projects/{projectId}/apps", JsonOptions.Web, ct) ?? [];
    }


    public async Task<MonitoredAppDto?> GetMonitoredAppAsync(int id)
    {
        return await Http.GetFromJsonAsync<MonitoredAppDto>($"api/appmonitoring/apps/{id}", JsonOptions.Web);
    }


    public async Task<List<AppHealthSampleDto>> GetMonitoredAppSamplesAsync(int id, int hours = 24)
    {
        return await Http.GetFromJsonAsync<List<AppHealthSampleDto>>($"api/appmonitoring/apps/{id}/samples?hours={hours}", JsonOptions.Web) ?? [];
    }


    public async Task<MonitoredAppDto?> CreateMonitoredAppAsync(int projectId, CreateMonitoredAppRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/appmonitoring/projects/{projectId}/apps", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<MonitoredAppDto>(JsonOptions.Web)
            : null;
    }


    public async Task<MonitoredAppDto?> UpdateMonitoredAppAsync(int id, UpdateMonitoredAppRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/appmonitoring/apps/{id}", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<MonitoredAppDto>(JsonOptions.Web)
            : null;
    }


    public async Task<ApiStatus> DeleteMonitoredAppAsync(int id)
    {
        var response = await Http.DeleteAsync($"api/appmonitoring/apps/{id}");
        return ApiStatus.From(response);
    }


    public async Task<AppMonitoringSummaryDto?> GetAppMonitoringSummaryAsync(CancellationToken ct = default)
    {
        return await Http.GetFromJsonAsync<AppMonitoringSummaryDto>("api/appmonitoring/summary", JsonOptions.Web, ct);
    }

    public async Task<IngestKeyResponse?> GenerateIngestKeyAsync(int appId)
    {
        var response = await Http.PostAsync($"api/appmonitoring/apps/{appId}/ingest-key", content: null);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<IngestKeyResponse>(JsonOptions.Web)
            : null;
    }


    public async Task<ApiStatus> RevokeIngestKeyAsync(int appId)
    {
        var response = await Http.DeleteAsync($"api/appmonitoring/apps/{appId}/ingest-key");
        return ApiStatus.From(response);
    }


    public async Task<List<string>> GetAppMetricNamesAsync(int appId)
    {
        return await Http.GetFromJsonAsync<List<string>>($"api/appmonitoring/apps/{appId}/metrics/names", JsonOptions.Web) ?? [];
    }


    public async Task<MetricSeriesDto?> GetAppMetricSeriesAsync(int appId, string metric, int hours = 24)
    {
        return await Http.GetFromJsonAsync<MetricSeriesDto>(
            $"api/appmonitoring/apps/{appId}/metrics/series?metric={Uri.EscapeDataString(metric)}&hours={hours}", JsonOptions.Web);
    }


    public async Task<AppVisitorSeriesDto?> GetAppVisitorSeriesAsync(int appId, int days = 30)
    {
        return await Http.GetFromJsonAsync<AppVisitorSeriesDto>(
            $"api/appmonitoring/apps/{appId}/visitors?days={days}", JsonOptions.Web);
    }


    public async Task<AppWebAnalyticsSummaryDto?> GetAppWebAnalyticsAsync(
        int appId,
        int days = 30,
        CancellationToken ct = default)
    {
        return await Http.GetFromJsonAsync<AppWebAnalyticsSummaryDto>(
            $"api/appmonitoring/apps/{appId}/web-analytics?days={days}",
            JsonOptions.Web,
            ct);
    }


    public async Task<AppWebAnalyticsConfigurationDto?> ConfigureAppWebAnalyticsAsync(
        int appId,
        ConfigureAppWebAnalyticsRequest request,
        CancellationToken ct = default)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/appmonitoring/apps/{appId}/web-analytics/configuration",
            request,
            JsonOptions.Web,
            ct);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<AppWebAnalyticsConfigurationDto>(JsonOptions.Web, ct)
            : null;
    }


    public async Task<AppWebAnalyticsConfigurationDto?> GetAppWebAnalyticsConfigurationAsync(
        int appId,
        CancellationToken ct = default)
    {
        return await Http.GetFromJsonAsync<AppWebAnalyticsConfigurationDto>(
            $"api/appmonitoring/apps/{appId}/web-analytics/configuration",
            JsonOptions.Web,
            ct);
    }


    public async Task<AppWebAnalyticsConfigurationDto?> RotateAppWebAnalyticsKeyAsync(
        int appId,
        CancellationToken ct = default)
    {
        var response = await Http.PostAsync(
            $"api/appmonitoring/apps/{appId}/web-analytics/rotate-key",
            content: null,
            ct);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<AppWebAnalyticsConfigurationDto>(JsonOptions.Web, ct)
            : null;
    }


    public async Task<List<AppMetricThresholdDto>> GetAppThresholdsAsync(int appId)
    {
        return await Http.GetFromJsonAsync<List<AppMetricThresholdDto>>($"api/appmonitoring/apps/{appId}/thresholds", JsonOptions.Web) ?? [];
    }


    public async Task<AppMetricThresholdDto?> CreateAppThresholdAsync(int appId, CreateMetricThresholdRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/appmonitoring/apps/{appId}/thresholds", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<AppMetricThresholdDto>(JsonOptions.Web)
            : null;
    }


    public async Task<ApiStatus> DeleteAppThresholdAsync(int thresholdId)
    {
        var response = await Http.DeleteAsync($"api/appmonitoring/thresholds/{thresholdId}");
        return ApiStatus.From(response);
    }


    public async Task<PaginatedResult<AppLogEntryDto>> GetAppLogsAsync(
        int appId, int hours = 24, int? minSeverity = null, string? search = null, int page = 1, int pageSize = 100)
    {
        var url = $"api/appmonitoring/apps/{appId}/logs?hours={hours}&page={page}&pageSize={pageSize}";
        if (minSeverity is { } ms) url += $"&minSeverity={ms}";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        return await Http.GetFromJsonAsync<PaginatedResult<AppLogEntryDto>>(url, JsonOptions.Web) ?? new();
    }


    public async Task<PaginatedResult<AppErrorEventDto>> GetAppErrorsAsync(int appId, int page = 1, int pageSize = 50)
    {
        return await Http.GetFromJsonAsync<PaginatedResult<AppErrorEventDto>>(
            $"api/appmonitoring/apps/{appId}/errors?page={page}&pageSize={pageSize}", JsonOptions.Web) ?? new();
    }

    public async Task<List<TaskLogDto>> GetTaskLogsAsync(int taskId)
    {
        return await Http.GetFromJsonAsync<List<TaskLogDto>>($"api/logs/task/{taskId}", JsonOptions.Web) ?? [];
    }


    public async Task<List<TaskLogDto>> GetTaskLogsAsync(int taskId, CancellationToken ct)
    {
        return await Http.GetFromJsonAsync<List<TaskLogDto>>($"api/logs/task/{taskId}", JsonOptions.Web, ct) ?? [];
    }


    public async Task<List<TaskLogDto>> GetTaskLogsUnmaskedAsync(int taskId)
    {
        return await Http.GetFromJsonAsync<List<TaskLogDto>>($"api/logs/task/{taskId}/unmasked", JsonOptions.Web) ?? [];
    }


    public async Task<List<TaskLogDto>> GetTaskLogsUnmaskedAsync(int taskId, CancellationToken ct)
    {
        return await Http.GetFromJsonAsync<List<TaskLogDto>>($"api/logs/task/{taskId}/unmasked", JsonOptions.Web, ct) ?? [];
    }

    public async Task<PaginatedResult<AuditLogDto>> GetAuditLogsAsync(int page = 1, int pageSize = 50, string? search = null, string? action = null, string? entityType = null, int? entityId = null, DateTime? dateFrom = null, DateTime? dateTo = null,
        string? sortBy = null, bool sortDescending = true)
    {
        var query = PageQuery(page, pageSize, search, sortBy, sortDescending);
        if (!string.IsNullOrEmpty(action)) query["action"] = action;
        if (!string.IsNullOrEmpty(entityType)) query["entityType"] = entityType;
        if (entityId.HasValue) query["entityId"] = entityId.Value.ToString();
        if (dateFrom.HasValue) query["dateFrom"] = dateFrom.Value.ToString("O");
        if (dateTo.HasValue) query["dateTo"] = dateTo.Value.ToString("O");
        return await Http.GetFromJsonAsync<PaginatedResult<AuditLogDto>>(QueryHelpers.AddQueryString("api/audit", query), JsonOptions.Web)
            ?? new PaginatedResult<AuditLogDto>();
    }


    public async Task<List<string>> GetAuditActionsAsync()
    {
        return await Http.GetFromJsonAsync<List<string>>("api/audit/actions", JsonOptions.Web) ?? [];
    }


    public async Task<List<string>> GetAuditEntityTypesAsync()
    {
        return await Http.GetFromJsonAsync<List<string>>("api/audit/entity-types", JsonOptions.Web) ?? [];
    }


    public async Task<AuditChainVerificationResult?> VerifyAuditChainAsync()
    {
        return await Http.GetFromJsonAsync<AuditChainVerificationResult>("api/audit/verify-chain", JsonOptions.Web);
    }


    public async Task<AuditChainVerificationResult?> VerifyAuditEntryAsync(int id)
    {
        var response = await Http.GetAsync($"api/audit/{id}/verify");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuditChainVerificationResult>(JsonOptions.Web);
    }


    public async Task<List<DashboardDto>> GetDashboardsAsync()
    {
        return await Http.GetFromJsonAsync<List<DashboardDto>>("api/dashboards", JsonOptions.Web) ?? [];
    }


    public async Task<DashboardDto?> GetDashboardAsync(int id, CancellationToken ct = default)
    {
        return await Http.GetFromJsonAsync<DashboardDto>($"api/dashboards/{id}", JsonOptions.Web, ct);
    }


    public Task<DashboardDto?> CreateDashboardAsync(CreateDashboardRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateDashboardRequest, DashboardDto>("api/dashboards", request, ct);


    public Task<DashboardDto?> UpdateDashboardAsync(int id, UpdateDashboardRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateDashboardRequest, DashboardDto>($"api/dashboards/{id}", request, ct);


    public Task<ApiStatus> DeleteDashboardAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/dashboards/{id}", ct);


    public async Task<List<AlertRuleDto>> GetAlertRulesAsync()
    {
        return await Http.GetFromJsonAsync<List<AlertRuleDto>>("api/alerts", JsonOptions.Web) ?? [];
    }


    public Task<AlertRuleDto?> CreateAlertRuleAsync(CreateAlertRuleRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateAlertRuleRequest, AlertRuleDto>("api/alerts", request, ct);


    public Task<AlertRuleDto?> UpdateAlertRuleAsync(int id, UpdateAlertRuleRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateAlertRuleRequest, AlertRuleDto>($"api/alerts/{id}", request, ct);


    public Task<ApiStatus> DeleteAlertRuleAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/alerts/{id}", ct);


    public async Task<PaginatedResult<NotificationChannelDto>> GetNotificationChannelsPagedAsync(
        int page = 1, int pageSize = 25, string? search = null,
        string? sortBy = null, bool sortDescending = false)
    {
        var query = PageQuery(page, pageSize, search, sortBy, sortDescending);
        return await GetJsonAsync<PaginatedResult<NotificationChannelDto>>(
            "api/notifications/channels", query).ConfigureAwait(false) ?? new();
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
        var response = await Http.GetAsync($"api/notifications/channels/{id}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NotificationChannelDto>(JsonOptions.Web);
    }


    public Task<NotificationChannelDto?> UpdateNotificationChannelAsync(int id, UpdateNotificationChannelRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateNotificationChannelRequest, NotificationChannelDto>($"api/notifications/channels/{id}", request, ct);


    public async Task<NotificationTestResultDto?> TestNotificationChannelAsync(int id)
    {
        var response = await Http.PostAsync($"api/notifications/channels/{id}/test", null);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NotificationTestResultDto>(JsonOptions.Web);
    }


    public async Task<PaginatedResult<NotificationRuleDto>> GetNotificationRulesPagedAsync(
        int page = 1, int pageSize = 25, string? search = null,
        string? sortBy = null, bool sortDescending = false)
    {
        var query = PageQuery(page, pageSize, search, sortBy, sortDescending);
        return await GetJsonAsync<PaginatedResult<NotificationRuleDto>>(
            "api/notifications/rules", query).ConfigureAwait(false) ?? new();
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
}
