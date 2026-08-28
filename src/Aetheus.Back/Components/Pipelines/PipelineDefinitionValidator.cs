// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Helpers;
using Aetheus.Shared.Validation;

namespace Aetheus.Back.Components.Pipelines;

internal static class PipelineDefinitionValidator
{
    private static readonly HashSet<string> KnownTypedStepTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "release", "substitute", "coverage", "complexity", "lint", "artifacts",
        "deploy", "apache-proxy", "apache-config", "certbot", "trigger", "restore-artifacts",
        "restore-backup", "scanner", "analysis-gate", "publish-observability", "ai", "smoke",
        "bluegreen-migrate", "bluegreen-up", "bluegreen-switch", "bluegreen-commit", "bluegreen-rollback",
        "bluegreen-retire"
    };

    internal static void ValidateDefinitionBasics(
        PipelineYamlDefinition definition,
        ICollection<string> errors,
        ICollection<string> warnings)
    {
        if (definition.VariableLibraries.Any(string.IsNullOrWhiteSpace))
            errors.Add("Variable library names cannot be empty.");
        if (definition.Vaults.Any(string.IsNullOrWhiteSpace))
            errors.Add("Vault names cannot be empty.");
        if (definition.Branches.Any(string.IsNullOrWhiteSpace))
            errors.Add("Branch filter entries cannot be empty.");
        if (!string.IsNullOrWhiteSpace(definition.SourceBranch)
            && !PipelineBranchValidator.IsValid(definition.SourceBranch))
            errors.Add("The configured source_branch is invalid.");
        if (definition.Branches.Count > 0
            && !string.Equals(definition.Trigger, "webhook", StringComparison.OrdinalIgnoreCase))
            warnings.Add("`branches:` filter only applies to the webhook trigger; it will be ignored for this trigger type.");
        if (definition.SupersedeRunning == true
            && !string.Equals(definition.Trigger, "webhook", StringComparison.OrdinalIgnoreCase))
            warnings.Add("`supersede_running:` only applies to the webhook trigger; it will be ignored for this trigger type.");
        if (definition.Stages.Count == 0)
            errors.Add("Pipeline must have at least one stage.");
    }

    internal static void ValidateStages(
        PipelineYamlDefinition definition,
        List<string> errors,
        List<string> warnings)
    {
        foreach (var stage in definition.Stages)
        {
            if (string.IsNullOrWhiteSpace(stage.Name))
                errors.Add("Stage name cannot be empty.");
            if (OsTypeHelper.IsUnrecognized(stage.Os))
                warnings.Add($"Stage '{stage.Name}' has an unrecognized os '{stage.Os}' - expected 'linux' or 'windows'; it will be ignored (no OS constraint).");
            if (stage.Jobs.Count > 0)
                ValidateJobs(stage, errors, warnings);
            else
                ValidateLegacyStage(stage, errors, warnings);
        }
    }

    private static void ValidateJobs(
        PipelineStageDefinition stage,
        List<string> errors,
        List<string> warnings)
    {
        foreach (var job in stage.Jobs)
        {
            if (string.IsNullOrWhiteSpace(job.Name))
                errors.Add($"Job name cannot be empty in stage '{stage.Name}'.");
            if (string.IsNullOrWhiteSpace(job.Agent)
                && string.IsNullOrWhiteSpace(job.Pool)
                && string.IsNullOrWhiteSpace(job.Environment)
                && string.IsNullOrWhiteSpace(stage.Agent)
                && string.IsNullOrWhiteSpace(stage.Pool)
                && string.IsNullOrWhiteSpace(stage.Environment))
                warnings.Add($"Job '{job.Name}' in stage '{stage.Name}' has no agent/pool/environment - will use run affinity.");
            if (OsTypeHelper.IsUnrecognized(job.Os))
                warnings.Add($"Job '{job.Name}' in stage '{stage.Name}' has an unrecognized os '{job.Os}' - expected 'linux' or 'windows'; it will be ignored (no OS constraint).");
            if (job.Steps.Count == 0)
                errors.Add($"Job '{job.Name}' in stage '{stage.Name}' must have at least one step.");
            ValidateSteps(
                errors, warnings, job.Steps,
                $"stage '{stage.Name}' / job '{job.Name}'", stage.Environment);
        }
    }

    private static void ValidateLegacyStage(
        PipelineStageDefinition stage,
        List<string> errors,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(stage.Agent)
            && string.IsNullOrWhiteSpace(stage.Pool)
            && string.IsNullOrWhiteSpace(stage.Environment))
            warnings.Add($"Stage '{stage.Name}' has no agent/pool/environment - will use run affinity.");
        if (stage.Steps.Count == 0)
            errors.Add($"Stage '{stage.Name}' must have at least one step.");
        ValidateSteps(errors, warnings, stage.Steps, $"stage '{stage.Name}'", stage.Environment);
    }

    private static void ValidateSteps(
        List<string> errors,
        List<string> warnings,
        List<PipelineStepDefinition> steps,
        string context,
        string? environment)
    {
        foreach (var step in steps)
        {
            ValidateStepBasics(errors, warnings, step, context, environment);
            ValidateApacheConfigStep(errors, step, context);
            ValidateRestoreArtifactsStep(errors, step, context);
            ValidateTriggerStep(errors, step, context);
        }
    }

    private static void ValidateStepBasics(
        List<string> errors,
        List<string> warnings,
        PipelineStepDefinition step,
        string context,
        string? environment)
    {
        if (string.IsNullOrWhiteSpace(step.Name))
            errors.Add($"Step name cannot be empty in {context}.");
        var isTypedStep = !string.IsNullOrWhiteSpace(step.Type)
            && KnownTypedStepTypes.Contains(step.Type);
        if (string.IsNullOrWhiteSpace(step.Shell) && !step.Checkout && !isTypedStep)
            errors.Add($"Step '{step.Name}' in {context} must have a shell command or checkout enabled.");
        if (step.RetryCount < 0)
            errors.Add($"Step '{step.Name}' retry_count cannot be negative.");
        PipelineAiStepValidator.Validate(errors, step, context);
        PipelineStepAnalysisValidator.Validate(errors, step, context, environment);
        if (string.Equals(step.Type, "coverage", StringComparison.OrdinalIgnoreCase)
            && step.MinCoverage is > 0)
            warnings.Add(
                $"Step '{step.Name}' in {context} uses legacy min_coverage. " +
                "Keep it temporarily for compatibility, then move the threshold to a versioned quality gate.");
    }

    private static void ValidateApacheConfigStep(
        ICollection<string> errors,
        PipelineStepDefinition step,
        string context)
    {
        if (!string.Equals(step.Type, "apache-config", StringComparison.OrdinalIgnoreCase))
            return;
        if (step.ConfigFiles.Count == 0)
            errors.Add($"Step '{step.Name}' in {context} requires at least one config_files entry.");
        foreach (var (siteFile, templatePath) in step.ConfigFiles)
        {
            if (!IsSafeApacheDestinationPattern(siteFile))
                errors.Add($"Step '{step.Name}' in {context} has an invalid Apache destination '{siteFile}'.");
            if (!PipelineConfigTemplateRenderer.IsSafeConfigPath(templatePath))
                errors.Add($"Step '{step.Name}' in {context} template '{templatePath}' must be under .pipeline/configs/.");
        }
    }

    private static void ValidateRestoreArtifactsStep(
        ICollection<string> errors,
        PipelineStepDefinition step,
        string context)
    {
        if (!string.Equals(step.Type, "restore-artifacts", StringComparison.OrdinalIgnoreCase))
            return;
        var hasReleaseSource = !string.IsNullOrWhiteSpace(step.Release);
        if (hasReleaseSource && !string.IsNullOrWhiteSpace(step.ArtifactSourcePipeline))
            errors.Add($"Step '{step.Name}' in {context} cannot combine 'release' with 'artifact_source_pipeline'.");
        else if (!hasReleaseSource
                 && (string.IsNullOrWhiteSpace(step.Artifact)
                     || string.IsNullOrWhiteSpace(step.ArtifactSourcePipeline)))
            errors.Add($"Step '{step.Name}' in {context} requires 'release' or both 'artifact' and 'artifact_source_pipeline'.");
        if (!PipelinePathValidation.IsSafeRelativeDirectory(step.TargetDirectory))
            errors.Add($"Step '{step.Name}' in {context} target_directory must be a safe relative path.");
        if (step.AllowMissing
            && string.IsNullOrWhiteSpace(step.ArtifactSourcePipeline)
            && !string.Equals(step.Release?.Trim(), "latest-published", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(step.Release?.Trim(), "previous-published", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(step.Release?.Trim(), "previous-deployed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(step.Release?.Trim(), "current-deployed", StringComparison.OrdinalIgnoreCase))
            errors.Add($"Step '{step.Name}' in {context} allow_missing requires artifact_source_pipeline or release: latest-published, previous-published, previous-deployed, or current-deployed.");
    }

    private static void ValidateTriggerStep(
        ICollection<string> errors,
        PipelineStepDefinition step,
        string context)
    {
        if (!string.Equals(step.Type, "trigger", StringComparison.OrdinalIgnoreCase))
            return;
        if (string.IsNullOrWhiteSpace(step.Pipeline))
            errors.Add($"Step '{step.Name}' in {context} requires a pipeline name.");
        if (step.InheritSource && !string.IsNullOrWhiteSpace(step.SourceBranch))
            errors.Add($"Step '{step.Name}' in {context} source_branch requires inherit_source: false.");
        if (step.InheritSource && !string.IsNullOrWhiteSpace(step.SourceCommit))
            errors.Add($"Step '{step.Name}' in {context} source_commit requires inherit_source: false.");
        if (!step.InheritSource && !PipelineBranchValidator.IsValid(step.SourceBranch ?? string.Empty))
            errors.Add($"Step '{step.Name}' in {context} requires a valid source_branch when inherit_source is false.");
    }

    private static bool IsSafeApacheDestinationPattern(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 220
            || value.Contains('/') || value.Contains('\\') || value.Contains("..", StringComparison.Ordinal))
            return false;
        var probe = System.Text.RegularExpressions.Regex.Replace(
            value, @"\$\([A-Za-z_][A-Za-z0-9_.-]*\)", "value");
        return OperationTargetValidator.ApacheSiteFileRegex().IsMatch(probe);
    }
}
