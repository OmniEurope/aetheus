// SPDX-License-Identifier: EUPL-1.2
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Components.Analysis;

public sealed class DependencyTrackClient(
    HttpClient http,
    IOptions<DependencyTrackOptions> options,
    TimeProvider timeProvider) : IDependencyTrackClient
{
    private readonly DependencyTrackOptions _options = options.Value;

    public async Task<DependencyTrackSubmission> SubmitAndReadAsync(
        string projectName,
        string projectVersion,
        string cycloneDxJson,
        CancellationToken ct)
    {
        var response = await http.PutAsJsonAsync("api/v1/bom", new
        {
            projectName,
            projectVersion,
            autoCreate = true,
            bom = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(cycloneDxJson))
        }, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var submission = await ReadDocumentAsync(response.Content, ct).ConfigureAwait(false);
        var token = submission.RootElement.TryGetProperty("token", out var tokenValue) ? tokenValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(token)) throw new HttpRequestException("Dependency-Track returned no BOM processing token.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.ProcessingTimeoutSeconds, 10, 600)));
        while (true)
        {
            var tokenResponse = await http.GetAsync($"api/v1/bom/token/{Uri.EscapeDataString(token)}", timeout.Token).ConfigureAwait(false);
            tokenResponse.EnsureSuccessStatusCode();
            using var tokenDocument = await ReadDocumentAsync(tokenResponse.Content, timeout.Token).ConfigureAwait(false);
            if (tokenDocument.RootElement.TryGetProperty("processing", out var processing)
                && processing.ValueKind == JsonValueKind.False) break;
            await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, timeout.Token).ConfigureAwait(false);
        }

        var lookup = await http.GetAsync(
            $"api/v1/project/lookup?name={Uri.EscapeDataString(projectName)}&version={Uri.EscapeDataString(projectVersion)}", ct).ConfigureAwait(false);
        lookup.EnsureSuccessStatusCode();
        using var project = await ReadDocumentAsync(lookup.Content, ct).ConfigureAwait(false);
        var projectId = project.RootElement.TryGetProperty("uuid", out var uuid) ? uuid.GetString() : null;
        if (string.IsNullOrWhiteSpace(projectId)) throw new HttpRequestException("Dependency-Track project lookup returned no UUID.");
        return new DependencyTrackSubmission(projectId, await GetVulnerabilitiesAsync(projectId, ct).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<DependencyTrackVulnerability>> GetVulnerabilitiesAsync(
        string externalProjectId,
        CancellationToken ct)
    {
        var response = await http.GetAsync($"api/v1/finding/project/{Uri.EscapeDataString(externalProjectId)}", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = await ReadDocumentAsync(response.Content, ct).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new HttpRequestException("Dependency-Track findings response is not an array.");
        var maximum = Math.Clamp(_options.MaxFindings, 1, 100_000);
        if (document.RootElement.GetArrayLength() > maximum)
            throw new HttpRequestException($"Dependency-Track returned more than the configured {maximum} findings.");
        var results = new List<DependencyTrackVulnerability>();
        foreach (var finding in document.RootElement.EnumerateArray())
        {
            var vulnerability = finding.TryGetProperty("vulnerability", out var vulnerabilityValue) ? vulnerabilityValue : default;
            var component = finding.TryGetProperty("component", out var componentValue) ? componentValue : default;
            var id = Read(vulnerability, "vulnId") ?? Read(vulnerability, "source") ?? "unknown";
            results.Add(new DependencyTrackVulnerability(
                id,
                Read(component, "name") ?? "unknown",
                Read(component, "version") ?? string.Empty,
                Read(component, "purl"),
                ParseSeverity(Read(vulnerability, "severity")),
                Read(finding, "analysis", "state") ?? "NOT_SET"));
        }
        return results;
    }

    private async Task<JsonDocument> ReadDocumentAsync(HttpContent content, CancellationToken ct)
    {
        var maximum = Math.Clamp(_options.MaxResponseBytes, 1_048_576, 104_857_600);
        if (content.Headers.ContentLength > maximum)
            throw new HttpRequestException($"Dependency-Track response exceeds the configured {maximum}-byte limit.");
        await using var source = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var bounded = new MemoryStream(Math.Min(maximum, 1_048_576));
        var buffer = new byte[81_920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0) break;
            if (bounded.Length + read > maximum)
                throw new HttpRequestException($"Dependency-Track response exceeds the configured {maximum}-byte limit.");
            await bounded.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
        bounded.Position = 0;
        return await JsonDocument.ParseAsync(bounded, new JsonDocumentOptions { MaxDepth = 64 }, ct).ConfigureAwait(false);
    }

    private static string? Read(JsonElement element, params string[] path)
    {
        foreach (var segment in path)
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element)) return null;
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static Aetheus.Shared.Components.Analysis.AnalysisSeverity ParseSeverity(string? value) => value?.ToUpperInvariant() switch
    {
        "CRITICAL" => Aetheus.Shared.Components.Analysis.AnalysisSeverity.Critical,
        "HIGH" => Aetheus.Shared.Components.Analysis.AnalysisSeverity.High,
        "MEDIUM" => Aetheus.Shared.Components.Analysis.AnalysisSeverity.Medium,
        "LOW" => Aetheus.Shared.Components.Analysis.AnalysisSeverity.Low,
        _ => Aetheus.Shared.Components.Analysis.AnalysisSeverity.Info
    };
}
