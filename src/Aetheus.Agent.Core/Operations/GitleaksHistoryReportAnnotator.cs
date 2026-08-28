// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json.Nodes;

namespace Aetheus.Agent.Core.Operations;

internal static class GitleaksHistoryReportAnnotator
{
    internal static async Task<bool> AnnotateAsync(
        ScannerManifestEntry scanner,
        string reportPath,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken ct)
    {
        if (!string.Equals(scanner.Key, "gitleaks-history", StringComparison.OrdinalIgnoreCase)) return true;
        if (!environment.TryGetValue("AETHEUS_GITLEAKS_HISTORY_MODE_RESOLVED", out var mode)
            || !environment.TryGetValue("AETHEUS_GITLEAKS_HEAD_SHA_RESOLVED", out var headSha)
            || !environment.TryGetValue("AETHEUS_GITLEAKS_HISTORY_REASON_RESOLVED", out var reason)) return false;

        var root = JsonNode.Parse(await File.ReadAllTextAsync(reportPath, ct).ConfigureAwait(false)) as JsonObject;
        if (root?["runs"] is not JsonArray { Count: > 0 } runs || runs[0] is not JsonObject run) return false;
        var properties = run["properties"] as JsonObject ?? new JsonObject();
        run["properties"] = properties;
        properties["aetheusHistory"] = new JsonObject
        {
            ["mode"] = mode,
            ["baseSha"] = environment.GetValueOrDefault("AETHEUS_GITLEAKS_BASE_SHA_RESOLVED"),
            ["headSha"] = headSha,
            ["reason"] = reason
        };
        await File.WriteAllTextAsync(reportPath, root.ToJsonString(), ct).ConfigureAwait(false);
        return true;
    }
}
