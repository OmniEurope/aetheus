// SPDX-License-Identifier: EUPL-1.2
using System.Net.Http.Json;
using System.Text.Json;
using Aetheus.Shared.DTOs;

namespace Aetheus.Agent.Core.Services;

public sealed class ServerApiClient(
    IHttpClientFactory httpClientFactory) : IServerApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public void SetBearerToken(string token)
    {
        // Token is stored in AgentState and applied by BearerTokenHandler
    }

    public async Task<ServerRegistrationResponse?> RegisterAsync(
        ServerRegistrationRequest request, CancellationToken ct = default)
    {
        return await PostAsync<ServerRegistrationResponse>("api/auth/register", request, ct).ConfigureAwait(false);
    }

    public async Task<ServerHeartbeatResponseDto?> SendHeartbeatAsync(int serverId, ServerHeartbeatDto heartbeat, CancellationToken ct = default)
    {
        using var client = httpClientFactory.CreateClient("AetheusServer");
        using var response = await client.PostAsJsonAsync($"api/servers/{serverId}/heartbeat", heartbeat, JsonOptions, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        // Tolerate an empty 200 body - e.g. a not-yet-upgraded backend that still
        // returns a bare Ok(). Empty simply means "no token renewal this beat".
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(body)
            ? null
            : JsonSerializer.Deserialize<ServerHeartbeatResponseDto>(body, JsonOptions);
    }

    public async Task<List<PendingTaskDto>> GetPendingTasksAsync(int serverId, int freeSlots, CancellationToken ct = default)
    {
        // take = free execution slots: the backend claims only what this agent can run now
        // (self-update tasks are claimed regardless - they bypass the concurrency gate).
        return await PostAsync<List<PendingTaskDto>>($"api/tasks/claim?serverId={serverId}&take={freeSlots}", new { }, ct).ConfigureAwait(false) ?? [];
    }

    public async Task StartTaskAsync(int taskId, CancellationToken ct = default)
    {
        await PostAsync($"api/tasks/{taskId}/start", new { }, ct).ConfigureAwait(false);
    }

    public async Task CompleteTaskAsync(int taskId, TaskResultDto result, CancellationToken ct = default)
    {
        await PostAsync($"api/tasks/{taskId}/complete", result, ct).ConfigureAwait(false);
    }

    public async Task ReportBackupResultAsync(int runId, BackupExecuteResultDto result, CancellationToken ct = default)
    {
        await PostAsync($"api/backups/runs/{runId}/result", result, ct).ConfigureAwait(false);
    }

    public async Task ReportRestoreCheckResultAsync(int runId, RestoreCheckResultDto result, CancellationToken ct = default)
    {
        await PostAsync($"api/backups/runs/{runId}/restore-check-result", result, ct).ConfigureAwait(false);
    }

    public async Task ReportUpdateProgressAsync(int serverId, AgentUpdateProgressReport report, CancellationToken ct = default)
    {
        // Best-effort: a 404 from a backend that predates item #3, a 401 during a token
        // renewal window, or a connection refused during shutdown must NOT bubble up to
        // the self-update executor - those would abort the actual update.
        try
        {
            using var client = httpClientFactory.CreateClient("AetheusServer");
            using var response = await client.PostAsJsonAsync(
                $"api/servers/{serverId}/agent/progress", report, JsonOptions, ct).ConfigureAwait(false);
            // Intentionally do not call EnsureSuccessAsync - silent failure is correct here.
        }
        catch (Exception)
        {
            // Telemetry-grade - swallowed by design.
        }
    }

    public async Task UploadArtifactAsync(int runId, string name, string? stageName, Stream zipContent, CancellationToken ct = default)
    {
        // Transfer client: no retry (the stream is partially consumed on failure) and a
        // timeout sized for multi-hundred-MB uploads, unlike the 30s RPC pipeline.
        using var client = httpClientFactory.CreateClient("AetheusServerTransfer");
        var query = $"api/artifacts/upload/{runId}?name={Uri.EscapeDataString(name)}";
        if (!string.IsNullOrEmpty(stageName))
            query += $"&stageName={Uri.EscapeDataString(stageName)}";

        using var content = new StreamContent(zipContent);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        using var request = new HttpRequestMessage(HttpMethod.Post, query) { Content = content };
        // The backend can reject a large artifact during the quota preflight. Waiting for
        // 100 Continue avoids sending hundreds of MB only to receive that deterministic rejection,
        // and prevents reverse proxies from surfacing the early close as a misleading 502.
        request.Headers.ExpectContinue = true;
        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    public async Task<Stream?> DownloadArtifactAsync(int artifactId, int deployRunId, CancellationToken ct = default)
    {
        using var client = httpClientFactory.CreateClient("AetheusServerTransfer");
        var url = $"api/artifacts/agent-download/{artifactId}?runId={deployRunId}";
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        // Buffer to a self-deleting temp file: a container image can be hundreds of MB, so we never
        // hold it in memory, and the HttpClient/response is disposed before the caller reads the bytes.
        var tempPath = Path.Combine(Path.GetTempPath(), $"prom-deploy-{artifactId}-{Guid.NewGuid():N}.zip");
        var temp = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await using var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await src.CopyToAsync(temp, ct).ConfigureAwait(false);
            temp.Position = 0;
            return temp;
        }
        catch
        {
            await temp.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ReleaseCreatedResponse?> CreateReleaseAsync(int projectId, int pipelineRunId, string version, string? changelog,
        string? commitHash = null, string? branch = null, int? artifactPipelineRunId = null,
        bool deployed = false, CancellationToken ct = default)
    {
        var request = new CreateReleaseRequest
        {
            Version = version,
            Changelog = changelog,
            PipelineRunId = pipelineRunId,
            ArtifactPipelineRunId = artifactPipelineRunId,
            CommitHash = commitHash,
            BranchName = branch,
            Deployed = deployed
        };
        return await PostAsync<ReleaseCreatedResponse>($"api/releases/create?projectId={projectId}", request, ct).ConfigureAwait(false);
    }

    public async Task PublishCoverageAsync(int runId, string xmlContent, string? stageName, string? stepName, CancellationToken ct = default)
    {
        var request = new { XmlContent = xmlContent, StageName = stageName, StepName = stepName };
        await PostAsync($"api/pipelines/runs/{runId}/coverage", request, ct).ConfigureAwait(false);
    }

    public async Task PublishLintAsync(int runId, string sarifContent, string? stageName, string? stepName, CancellationToken ct = default)
    {
        var request = new { SarifContent = sarifContent, StageName = stageName, StepName = stepName };
        await PostAsync($"api/pipelines/runs/{runId}/lint", request, ct).ConfigureAwait(false);
    }

    public async Task PublishComplexityAsync(int runId, double avgCyclomatic, int maxCyclomatic, int totalMethods, int highComplexityMethods, int totalLinesOfCode, string? stageName, CancellationToken ct = default)
    {
        var request = new
        {
            AvgCyclomatic = avgCyclomatic,
            MaxCyclomatic = maxCyclomatic,
            TotalMethods = totalMethods,
            HighComplexityMethods = highComplexityMethods,
            TotalLinesOfCode = totalLinesOfCode,
            StageName = stageName
        };
        await PostAsync($"api/pipelines/runs/{runId}/complexity", request, ct).ConfigureAwait(false);
    }

    public async Task AppendLogAsync(AppendLogRequest log, CancellationToken ct = default)
    {
        await PostAsync("api/logs", log, ct).ConfigureAwait(false);
    }

    public async Task AppendLogBatchAsync(List<AppendLogRequest> logs, CancellationToken ct = default)
    {
        await PostAsync("api/logs/batch", logs, ct).ConfigureAwait(false);
    }

    public async Task<Dictionary<int, string>> GetTaskStatusesAsync(List<int> taskIds, CancellationToken ct = default)
    {
        return await PostAsync<Dictionary<int, string>>("api/tasks/statuses", taskIds, ct).ConfigureAwait(false) ?? new();
    }

    public async Task<List<AppProbeConfigDto>> GetAppProbesAsync(CancellationToken ct = default)
    {
        using var client = httpClientFactory.CreateClient("AetheusServer");
        using var response = await client.GetAsync("api/appmonitoring/agent/probes", ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<AppProbeConfigDto>>(JsonOptions, ct).ConfigureAwait(false) ?? [];
    }

    public async Task ReportAppProbeResultsAsync(List<AppProbeResultDto> results, CancellationToken ct = default)
    {
        await PostAsync("api/appmonitoring/agent/probe-results", results, ct).ConfigureAwait(false);
    }

    private async Task<T?> PostAsync<T>(string url, object body, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient("AetheusServer");
        using var response = await client.PostAsJsonAsync(url, body, JsonOptions, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
    }

    private async Task PostAsync(string url, object body, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient("AetheusServer");
        using var response = await client.PostAsJsonAsync(url, body, JsonOptions, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    // Surfaces the response body in the exception message so agent logs reveal which model
    // validation rule (or auth issue) the server rejected - otherwise heartbeats fail silently
    // with just the status code.
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string? body = null;
        try { body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
        catch { /* best-effort */ }
        var trimmed = string.IsNullOrWhiteSpace(body) ? "<empty>" : body.Length > 1024 ? body[..1024] + "…" : body;
        throw new HttpRequestException(
            $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} - {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}: {trimmed}",
            inner: null,
            statusCode: response.StatusCode);
    }
}
