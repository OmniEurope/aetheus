// SPDX-License-Identifier: EUPL-1.2
using YamlDotNet.RepresentationModel;

namespace Aetheus.Back.Components.Pipelines;

internal static class PipelineYamlDiagnostics
{
    private static readonly HashSet<string> KnownTopLevelKeys =
    [
        "name", "trigger", "schedule", "source_branch", "branches", "supersede_running", "on_success", "extends", "parameters",
        "variables", "variable_libraries", "vaults", "stages"
    ];

    private static readonly HashSet<string> KnownStageKeys =
    [
        "name", "agent", "os", "group", "environment", "pool", "execution_role", "condition",
        "depends_on", "variables", "steps", "jobs", "matrix", "strategy", "artifacts", "remove"
    ];

    private static readonly HashSet<string> KnownJobKeys =
    [
        "name", "agent", "os", "pool", "environment", "execution_role", "condition",
        "variables", "steps", "matrix", "strategy", "artifacts", "remove"
    ];

    private static readonly HashSet<string> KnownStepKeys =
    [
        "name", "shell", "condition", "checkout", "working_directory", "timeout_seconds",
        "retry_count", "continue_on_error", "type", "version", "changelog", "target_files",
        "pipeline", "variables", "inherit_source", "source_branch", "source_commit", "artifact", "artifact_source_pipeline", "release", "target_directory", "allow_missing", "app",
        "compose", "health_timeout_seconds", "health_url", "backup_run", "min_coverage",
        "coverage_tool", "coverage_language", "coverage_version", "max_complexity", "scanner", "analysis_scope",
        "analysis_preset", "analysis_rules", "analysis_grading",
        "target_url", "target_classification", "active", "api_specification_url", "api_specification_format",
        "config_files", "remove"
    ];

    public static void AppendUnknownPropertyWarnings(string yaml, List<string> warnings, ILogger logger)
    {
        try
        {
            using var reader = new StringReader(yaml);
            var yamlStream = new YamlStream();
            yamlStream.Load(reader);
            if (yamlStream.Documents.Count == 0 || yamlStream.Documents[0].RootNode is not YamlMappingNode root)
                return;

            AppendMappingWarnings(root, KnownTopLevelKeys, "top-level", warnings);
            var stagesNode = root.Children.FirstOrDefault(k => Scalar(k.Key) == "stages").Value;
            if (stagesNode is not YamlSequenceNode stages)
                return;

            foreach (var stage in stages.Children.OfType<YamlMappingNode>())
            {
                var stageName = Scalar(stage.Children.FirstOrDefault(k => Scalar(k.Key) == "name").Value) ?? "?";
                AppendMappingWarnings(stage, KnownStageKeys, $"stage '{stageName}'", warnings);
                AppendStepWarnings(stage, "steps", warnings);

                var jobsNode = stage.Children.FirstOrDefault(k => Scalar(k.Key) == "jobs").Value;
                if (jobsNode is not YamlSequenceNode jobs)
                    continue;
                foreach (var job in jobs.Children.OfType<YamlMappingNode>())
                {
                    AppendMappingWarnings(job, KnownJobKeys, "job", warnings);
                    AppendStepWarnings(job, "steps", warnings);
                }
            }
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or InvalidCastException or InvalidOperationException)
        {
            logger.LogDebug(ex, "YAML DOM walk for unknown-key warnings failed");
        }
    }

    private static void AppendStepWarnings(YamlMappingNode parent, string key, List<string> warnings)
    {
        var node = parent.Children.FirstOrDefault(item => Scalar(item.Key) == key).Value;
        if (node is not YamlSequenceNode steps)
            return;
        foreach (var step in steps.Children.OfType<YamlMappingNode>())
            AppendMappingWarnings(step, KnownStepKeys, "step", warnings);
    }

    private static void AppendMappingWarnings(
        YamlMappingNode node, HashSet<string> knownKeys, string context, List<string> warnings)
    {
        foreach (var item in node.Children)
        {
            var key = Scalar(item.Key) ?? string.Empty;
            if (!knownKeys.Contains(key))
                warnings.Add($"Unknown {context} property '{key}' will be ignored.");
        }
    }

    private static string? Scalar(YamlNode? node) => (node as YamlScalarNode)?.Value;
}
