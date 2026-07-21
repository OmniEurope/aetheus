// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Data.Entities;
using Microsoft.Extensions.Logging;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Parses a SARIF 2.1.0 report into a <see cref="LintResult"/>, counting results by severity level.
/// SARIF is the de-facto static-analysis interchange format emitted by ESLint, dotnet analyzers,
/// roslynator, and most linters via <c>--format sarif</c>. Mirrors <see cref="CoverageResultParser"/>.
/// </summary>
public static class LintResultParser
{
    public static LintResult? Parse(string sarifContent, int runId, string? stageName, string? stepName, ILogger? logger = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(sarifContent);
            if (!doc.RootElement.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
                return null;

            int errors = 0, warnings = 0, info = 0;
            string? tool = null;

            foreach (var run in runs.EnumerateArray())
            {
                tool ??= run.TryGetProperty("tool", out var t)
                    && t.TryGetProperty("driver", out var d)
                    && d.TryGetProperty("name", out var n)
                    ? n.GetString()
                    : null;

                if (!run.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var result in results.EnumerateArray())
                {
                    // SARIF defaults an absent level to "warning".
                    var level = result.TryGetProperty("level", out var l) ? l.GetString() : "warning";
                    switch (level)
                    {
                        case "error": errors++; break;
                        case "note": info++; break;
                        case "none": info++; break;
                        default: warnings++; break;
                    }
                }
            }

            return new LintResult
            {
                PipelineRunId = runId,
                StageName = stageName,
                StepName = stepName,
                Tool = tool,
                ErrorCount = errors,
                WarningCount = warnings,
                InfoCount = info
            };
        }
        catch (JsonException ex)
        {
            logger?.LogWarning("LintResultParser: malformed SARIF (run {RunId}): {Message}", runId, ex.Message);
            return null;
        }
    }
}
