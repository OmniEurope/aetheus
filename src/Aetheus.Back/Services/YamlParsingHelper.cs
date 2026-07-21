// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aetheus.Back.Services;

public static class YamlParsingHelper
{
    public static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    /// <summary>
    /// Parses a pipeline YAML document and applies the lightweight semantic checks shared by
    /// PipelineService and PipelineRunService. Returns <c>null</c> when the YAML is invalid.
    /// </summary>
    public static PipelineYamlDefinition? ParseAndValidate(string yaml, ILogger? logger = null)
    {
        try
        {
            var definition = Deserializer.Deserialize<PipelineYamlDefinition>(yaml);
            if (definition is null) return null;

            if (definition.VariableLibraries.Any(string.IsNullOrWhiteSpace)) return null;
            if (definition.Vaults.Any(string.IsNullOrWhiteSpace)) return null;

            return definition;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            logger?.LogWarning(ex, "Unexpected error parsing pipeline YAML");
            return null;
        }
    }

    /// <summary>
    /// Flattens stages that use the <c>jobs</c> property into a flat list of effective stages.
    /// Each job becomes its own pseudo-stage with <c>GroupName = parent stage name</c>.
    /// Jobs within a stage run in parallel by default: they all depend on the prerequisite stages'
    /// jobs. A job may also declare <c>depends_on</c> on sibling job names to form an intra-stage
    /// DAG (S-TECH-55) - those sibling names map directly to pseudo-stage names. Cross-stage
    /// <c>depends_on</c> resolves to ALL jobs of the referenced stage so the next stage waits for
    /// every parallel job to complete.
    /// Stages using the legacy <c>steps</c> format pass through unchanged.
    /// </summary>
    public static List<PipelineStageDefinition> FlattenJobs(PipelineYamlDefinition definition)
    {
        // Build a map: stage name → all job names (for resolving cross-stage depends_on)
        var allJobsPerStage = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in definition.Stages)
        {
            allJobsPerStage[s.Name] = s.Jobs.Count > 0
                ? s.Jobs.Select(j => j.Name).ToList()
                : [s.Name];
        }

        var result = new List<PipelineStageDefinition>();
        foreach (var stage in definition.Stages)
        {
            if (stage.Jobs.Count > 0)
            {
                var externalDeps = stage.DependsOn
                    .SelectMany(dep => allJobsPerStage.GetValueOrDefault(dep, [dep]))
                    .ToList();

                foreach (var job in stage.Jobs)
                {
                    var mergedVars = new Dictionary<string, string>(stage.Variables, StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in job.Variables)
                        mergedVars[kv.Key] = kv.Value;

                    // S-TECH-55: a job's own depends_on lists sibling job names within this stage.
                    // Sibling job names already equal their pseudo-stage names, so they merge straight
                    // in alongside the stage-level (cross-stage) dependencies.
                    var jobDeps = job.DependsOn.Count > 0
                        ? externalDeps.Concat(job.DependsOn).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                        : externalDeps;

                    result.Add(new PipelineStageDefinition
                    {
                        Name = job.Name,
                        Agent = !string.IsNullOrEmpty(job.Agent) ? job.Agent : stage.Agent,
                        Os = !string.IsNullOrEmpty(job.Os) ? job.Os : stage.Os,
                        Group = stage.Name,
                        ExecutionRole = job.ExecutionRole ?? stage.ExecutionRole,
                        Environment = job.Environment ?? stage.Environment,
                        Pool = job.Pool ?? stage.Pool,
                        Condition = job.Condition ?? stage.Condition,
                        DependsOn = jobDeps,
                        Variables = mergedVars,
                        Steps = job.Steps,
                        Matrix = job.Matrix ?? stage.Matrix,
                        Strategy = job.Strategy ?? stage.Strategy,
                        Artifacts = job.Artifacts.Count > 0 ? job.Artifacts : stage.Artifacts,
                        Isolation = job.Isolation ?? stage.Isolation ?? definition.Isolation
                    });
                }
            }
            else
            {
                // Direct-step stage inherits the run-level isolation when it sets none of its own.
                result.Add(stage.Isolation is null && definition.Isolation is not null
                    ? stage with { Isolation = definition.Isolation }
                    : stage);
            }
        }
        return result;
    }

    public static List<string> ParseVaultNames(string yamlDefinition)
    {
        if (string.IsNullOrWhiteSpace(yamlDefinition))
            return [];

        try
        {
            var def = Deserializer.Deserialize<PipelineYamlDefinition>(yamlDefinition);
            return def?.Vaults?
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }
}
