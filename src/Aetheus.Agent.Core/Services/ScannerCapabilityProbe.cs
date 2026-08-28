// SPDX-License-Identifier: EUPL-1.2
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Aetheus.Agent.Core.Services;

public static class ScannerCapabilityProbe
{
    public static async Task<List<string>> CollectAsync(
        IShellRunner shell,
        string workDirectory,
        TimeProvider timeProvider,
        CancellationToken ct = default)
    {
        var diagnostics = new List<string>();
        var scansDirectory = Path.Combine(workDirectory, "scans");
        var residualWorkspaces = Directory.Exists(scansDirectory)
            ? Directory.EnumerateDirectories(scansDirectory).Take(101).Count()
            : 0;
        diagnostics.Add(residualWorkspaces == 0
            ? "scanner-cleanup:ready:0 residual workspaces"
            : $"scanner-cleanup:degraded:{residualWorkspaces} residual workspaces require operator review");
        diagnostics.Add($"scanner-manifest:sha256:{ScannerManifestCatalog.Sha256}");
        await AddTrivyDatabaseDiagnosticAsync(diagnostics, workDirectory, timeProvider, ct).ConfigureAwait(false);
        var dockerAvailable = await DockerProbe.IsAvailableAsync(shell, ct).ConfigureAwait(false);
        foreach (var scanner in ScannerManifestCatalog.Default.Scanners)
        {
            ct.ThrowIfCancellationRequested();
            if (scanner.Execution == "container")
            {
                if (!dockerAvailable)
                {
                    diagnostics.Add($"scanner:{scanner.Key}:{scanner.Version}:unavailable:docker daemon or socket unavailable");
                    continue;
                }
                try
                {
                    var result = await shell.RunExecAsync(
                        "docker", ["image", "inspect", scanner.Image!], ct,
                        AgentRuntimeDefaults.CapabilityProbeTimeout).ConfigureAwait(false);
                    diagnostics.Add(result.ExitCode == 0
                        ? $"scanner:{scanner.Key}:{scanner.Version}:ready:immutable image cached"
                        : $"scanner:{scanner.Key}:{scanner.Version}:available-on-demand:immutable image pull required");
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    diagnostics.Add($"scanner:{scanner.Key}:{scanner.Version}:unavailable:container image probe failed");
                }
                continue;
            }

            if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                diagnostics.Add($"scanner:{scanner.Key}:{scanner.Version}:unavailable:verified binary supports Linux x64 only");
                continue;
            }
            if (!dockerAvailable)
            {
                diagnostics.Add($"scanner:{scanner.Key}:{scanner.Version}:unavailable:container runtime required for binary confinement");
                continue;
            }
            var path = Path.Combine(workDirectory, "scanners", scanner.Key, scanner.Version, scanner.Key);
            if (!File.Exists(path))
            {
                diagnostics.Add($"scanner:{scanner.Key}:{scanner.Version}:available-on-demand:verified binary download required");
                continue;
            }
            await using var stream = File.OpenRead(path);
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
            diagnostics.Add(string.Equals(hash, scanner.Sha256LinuxAmd64, StringComparison.OrdinalIgnoreCase)
                ? $"scanner:{scanner.Key}:{scanner.Version}:ready:verified binary cached"
                : $"scanner:{scanner.Key}:{scanner.Version}:degraded:cached binary hash mismatch; redownload required");
        }
        return diagnostics;
    }

    private static async Task AddTrivyDatabaseDiagnosticAsync(
        List<string> diagnostics,
        string workDirectory,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var metadataPath = Path.Combine(workDirectory, "scanner-cache", "trivy", "db", "metadata.json");
        if (!File.Exists(metadataPath))
        {
            diagnostics.Add("scanner-db:trivy:not-initialized:no vulnerability database cached");
            return;
        }
        try
        {
            var info = new FileInfo(metadataPath);
            if (info.Length is <= 0 or > 65_536)
            {
                diagnostics.Add("scanner-db:trivy:degraded:database metadata size is invalid");
                return;
            }
            await using var stream = File.OpenRead(metadataPath);
            using var document = await JsonDocument.ParseAsync(stream,
                new JsonDocumentOptions { MaxDepth = 8 }, ct).ConfigureAwait(false);
            var updatedAt = ReadTimestamp(document.RootElement, "UpdatedAt")
                ?? ReadTimestamp(document.RootElement, "DownloadedAt");
            if (!updatedAt.HasValue)
            {
                diagnostics.Add("scanner-db:trivy:degraded:database metadata has no update timestamp");
                return;
            }
            var age = timeProvider.GetUtcNow() - updatedAt.Value;
            diagnostics.Add(age > TimeSpan.FromHours(48)
                ? $"scanner-db:trivy:degraded:database is {Math.Floor(age.TotalHours)} hours old"
                : $"scanner-db:trivy:ready:updated={updatedAt:O}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            diagnostics.Add("scanner-db:trivy:degraded:database metadata is unreadable");
        }
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                || property.Value.ValueKind != JsonValueKind.String)
                continue;
            return DateTimeOffset.TryParse(property.Value.GetString(), out var timestamp)
                ? timestamp.ToUniversalTime()
                : null;
        }
        return null;
    }
}
