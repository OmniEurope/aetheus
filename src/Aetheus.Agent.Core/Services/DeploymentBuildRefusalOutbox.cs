// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Agent.Core.Services;

public interface IDeploymentBuildRefusalOutbox
{
    Task StoreAsync(DeploymentBuildRefusalReport report, CancellationToken ct);
    Task<IReadOnlyList<DeploymentBuildRefusalReport>> ReadAllAsync(CancellationToken ct);
    void Remove(Guid incidentId);
}

public sealed class DeploymentBuildRefusalOutbox(
    IOptions<AetheusAgentOptions> options,
    ILogger<DeploymentBuildRefusalOutbox> logger)
    : IDeploymentBuildRefusalOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directory = Path.Combine(
        options.Value.WorkDirectory,
        "outbox",
        "deployment-build-refusals");

    public async Task StoreAsync(DeploymentBuildRefusalReport report, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);
        var path = PathFor(report.IncidentId);
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, report, JsonOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public async Task<IReadOnlyList<DeploymentBuildRefusalReport>> ReadAllAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_directory)) return [];
        var reports = new List<DeploymentBuildRefusalReport>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json").Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
                var report = await JsonSerializer.DeserializeAsync<DeploymentBuildRefusalReport>(
                    stream, JsonOptions, ct).ConfigureAwait(false);
                if (report is not null) reports.Add(report);
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Ignoring corrupt deployment-build-refusal outbox item {Path}", path);
            }
        }
        return reports;
    }

    public void Remove(Guid incidentId)
    {
        var path = PathFor(incidentId);
        if (File.Exists(path)) File.Delete(path);
    }

    private string PathFor(Guid incidentId) =>
        Path.Combine(_directory, incidentId.ToString("N") + ".json");
}
