// SPDX-License-Identifier: EUPL-1.2
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aetheus.Agent.Core.Operations;

public sealed class AnalysisGateOperationExecutor(IServerApiClient apiClient) : IOperationExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public bool CanHandle(OperationKind kind) => kind == OperationKind.PipelineEvaluateAnalysisGate;

    public Task<ExecutorResult> ExecuteAsync(
        OperationKind kind,
        string target,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken) =>
        ExecuteAsync(kind, target, new Dictionary<string, string>(), timeoutSeconds, onOutput, cancellationToken);

    public async Task<ExecutorResult> ExecuteAsync(
        OperationKind kind,
        string target,
        IReadOnlyDictionary<string, string> envVars,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
    {
        if (kind != OperationKind.PipelineEvaluateAnalysisGate
            || target.ToLowerInvariant() is not ("security" or "quality")
            || !envVars.TryGetValue("AETHEUS_RUN_ID", out var runText)
            || !int.TryParse(runText, out var runId)
            || runId <= 0)
            return new ExecutorResult(1, false);

        var gate = await apiClient.GetAnalysisRunGateAsync(
            runId, target, cancellationToken).ConfigureAwait(false);
        if (gate is null)
        {
            await onOutput("Le verdict d'analyse agrégé est indisponible.", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(1, false);
        }

        var stageName = envVars.GetValueOrDefault("AETHEUS_STAGE_NAME");
        var machine = JsonSerializer.Serialize(gate, JsonOptions);
        var human = BuildMarkdown(target, gate);
        await AnalysisArtifactUploader.UploadTextAsync(apiClient, runId,
            $"analysis-{target}-summary-json", stageName, $"{target}-summary.json", machine, cancellationToken).ConfigureAwait(false);
        await AnalysisArtifactUploader.UploadTextAsync(apiClient, runId,
            $"analysis-{target}-summary", stageName, $"{target}-summary.md", human, cancellationToken).ConfigureAwait(false);

        var level = gate.Status == AnalysisGateStatus.Error
            ? TaskLogLevel.Error
            : gate.Status is AnalysisGateStatus.Warning or AnalysisGateStatus.Blocked
                ? TaskLogLevel.Warning
                : TaskLogLevel.Info;
        await onOutput(
            $"Verdict {target}: {gate.Status} - {gate.ReportCount} rapports, {gate.FindingCount} findings, "
            + $"{gate.BlockerCount} blocages, {gate.WarningCount} avertissements, "
            + $"note {gate.Grade?.OverallGrade?.ToString() ?? "incomplète"}.", level).ConfigureAwait(false);
        // A blocked policy verdict is valid evidence and must degrade the assurance grade without
        // masquerading as a producer failure. Only unavailable/incomplete gate evidence is technical.
        return new ExecutorResult(gate.Status == AnalysisGateStatus.Error ? 1 : 0, false);
    }

    private static string BuildMarkdown(string scope, AnalysisRunGateDto gate)
    {
        var title = scope == "security" ? "Sécurité" : "Qualité";
        var text = new StringBuilder()
            .AppendLine($"# Synthèse {title}")
            .AppendLine()
            .AppendLine($"- Run : [{gate.PipelineRunId}](/pipelines/runs/{gate.PipelineRunId})")
            .AppendLine($"- Verdict : **{gate.Status}**")
            .AppendLine($"- Note globale : **{gate.Grade?.OverallGrade?.ToString() ?? "incomplète"}**")
            .AppendLine($"- Domaine limitant : {gate.Grade?.LimitingDomain?.ToString() ?? "indisponible"}")
            .AppendLine($"- Snapshot du barème : `{gate.Grade?.SnapshotHash ?? "indisponible"}`")
            .AppendLine($"- Rapports : {gate.ReportCount}")
            .AppendLine($"- Findings : {gate.FindingCount}, dont {gate.NewFindingCount} nouveaux")
            .AppendLine($"- Composants : {gate.ComponentCount}")
            .AppendLine($"- Métriques : {gate.MetricCount}")
            .AppendLine($"- Blocages : {gate.BlockerCount}; avertissements : {gate.WarningCount}")
            .AppendLine($"- Producteurs manquants : {(gate.MissingProducers.Count == 0 ? "aucun" : string.Join(", ", gate.MissingProducers))}");
        AppendGrades(text, gate);
        AppendViolations(text, gate);
        AppendReports(text, gate);
        AppendFindings(text, gate);
        return text.ToString();
    }

    private static void AppendGrades(StringBuilder text, AnalysisRunGateDto gate)
    {
        text.AppendLine()
            .AppendLine("## Notes par domaine")
            .AppendLine()
            .AppendLine("| Domaine | Note | Complétude | Obligatoire |")
            .AppendLine("|---|---:|---|---|");
        if (gate.Grade is null)
            text.AppendLine("| Non configuré | - | Incomplete | - |");
        else
            foreach (var domain in gate.Grade.Domains)
                text.AppendLine($"| {domain.Domain} | {domain.Grade?.ToString() ?? "-"} | {domain.Completeness} | {(domain.Required ? "oui" : "non")} |");
    }

    private static void AppendViolations(StringBuilder text, AnalysisRunGateDto gate)
    {
        text.AppendLine()
            .AppendLine("## Règles violées")
            .AppendLine()
            .AppendLine("| Règle | Résultat | Valeur observée | Condition | Origine | Rapport |")
            .AppendLine("|---|---|---:|---|---|---|");
        if (gate.Violations.Count == 0)
            text.AppendLine("| Aucune | - | - | - | - | - |");
        else
            foreach (var violation in gate.Violations)
            {
                var condition = $"{violation.Operator ?? "-"} {violation.Threshold ?? "-"}";
                text.AppendLine(
                    $"| {Escape(violation.PolicyKey)} | {violation.Outcome} | " +
                    $"{Escape(violation.ObservedValue ?? "-")} | {Escape(condition)} | " +
                    $"{violation.Scope} v{violation.Version} | " +
                    $"[#{violation.ReportId}](/pipelines/runs/{gate.PipelineRunId}?tab=analysis) |");
            }
    }

    private static void AppendReports(StringBuilder text, AnalysisRunGateDto gate)
    {
        text.AppendLine()
            .AppendLine("## Rapports")
            .AppendLine()
            .AppendLine("| Scanner | Version | Statut | Gate | Artefact |")
            .AppendLine("|---|---:|---|---|---|");
        foreach (var report in gate.Reports)
        {
            var artifact = report.PipelineArtifactId.HasValue
                ? $"[#{report.PipelineArtifactId}](/pipelines/runs/{gate.PipelineRunId}?tab=artifacts)"
                : "-";
            text.AppendLine($"| {Escape(report.ScannerName)} | {Escape(report.ScannerVersion)} | {report.ReportStatus} | {report.GateStatus?.ToString() ?? "Error"} | {artifact} |");
        }
    }

    private static void AppendFindings(StringBuilder text, AnalysisRunGateDto gate)
    {
        text.AppendLine().AppendLine("## Findings").AppendLine();
        if (gate.Findings.Count == 0) text.AppendLine("Aucun finding.");
        else
            foreach (var finding in gate.Findings)
                text.AppendLine($"- [{finding.Severity} · {Escape(finding.RuleId)} · {Escape(finding.Title)}](/analysis/findings/{finding.FindingId}){(finding.IsNew ? " - nouveau" : string.Empty)}");
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
