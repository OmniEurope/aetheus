// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.AppMonitoring;

public interface IAppTelemetryService
{
    Task<IngestKeyResponse?> GenerateIngestKeyAsync(int appId, CancellationToken ct = default);
    Task<bool> RevokeIngestKeyAsync(int appId, CancellationToken ct = default);

    Task<List<string>> GetMetricNamesAsync(int appId, CancellationToken ct = default);
    Task<MetricSeriesDto> GetMetricSeriesAsync(int appId, string metricName, int hours, CancellationToken ct = default);
    Task<AppVisitorSeriesDto> GetVisitorSeriesAsync(int appId, int days, CancellationToken ct = default);

    Task<List<AppMetricThresholdDto>> GetThresholdsAsync(int appId, CancellationToken ct = default);
    Task<AppMetricThresholdDto> CreateThresholdAsync(int appId, CreateMetricThresholdRequest request, CancellationToken ct = default);
    Task<int?> GetThresholdAppIdAsync(int thresholdId, CancellationToken ct = default);
    Task<bool> DeleteThresholdAsync(int thresholdId, CancellationToken ct = default);

    Task<PaginatedResult<AppLogEntryDto>> GetLogsAsync(int appId, int hours, int? minSeverity, string? search, int page, int pageSize, CancellationToken ct = default);
    Task<PaginatedResult<AppErrorEventDto>> GetErrorsAsync(int appId, int page, int pageSize, CancellationToken ct = default);
}
