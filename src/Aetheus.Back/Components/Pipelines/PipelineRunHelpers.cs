// SPDX-License-Identifier: EUPL-1.2
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Stateless helpers for the pipeline-run engine (<see cref="PipelineRunService"/>): variable
/// substitution/parsing, DTO mapping, condition evaluation, matrix expansion, isolation-policy checks
/// and the per-step env scoping. Extracted from the former <c>PipelineRunService.Helpers.cs</c> /
/// <c>.Tasks.cs</c> partials into a real collaborator. <c>partial</c> here is required only for the
/// <c>[GeneratedRegex]</c> source generator.
/// </summary>
public static partial class PipelineRunHelpers
{
    public const int MaxMatrixAxes = 8;
    public const int MaxMatrixValuesPerAxis = 32;
    public const int MaxMatrixLegs = 256;

    // --- Condition / output-variable regexes ---

    [GeneratedRegex(@"eq\s*\(\s*variables\['(\w+)'\]\s*,\s*'([^']*)'\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex EqConditionPattern();

    [GeneratedRegex(@"ne\s*\(\s*variables\['(\w+)'\]\s*,\s*'([^']*)'\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex NeConditionPattern();

    [GeneratedRegex(@"contains\s*\(\s*variables\['(\w+)'\]\s*,\s*'([^']*)'\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex ContainsConditionPattern();

    [GeneratedRegex(@"##aetheus\[setvariable name=(\w+)\](.+)$", RegexOptions.Multiline)]
    private static partial Regex OutputVariablePattern();

    [GeneratedRegex(@"^\d+(\.\d+)?\s*[bkmgBKMG]?$")]
    private static partial Regex MemoryLimitPattern();

    [GeneratedRegex(@"^\d+(\.\d+)?$")]
    private static partial Regex CpusLimitPattern();

    // --- Variable substitution ($(VarName) syntax) ---

    public static string SubstituteVariables(string command, Dictionary<string, string> variables)
        => PipelineCommandBuilder.Substitute(command, variables);

    /// <summary>Returns a copy of <paramref name="vars"/> with all keys flagged as secrets removed.</summary>
    public static Dictionary<string, string> FilterSecretKeys(Dictionary<string, string> vars, HashSet<string> secretKeys)
    {
        if (secretKeys.Count == 0) return vars;
        var filtered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in vars)
        {
            if (!secretKeys.Contains(k)) filtered[k] = v;
        }
        return filtered;
    }

    /// <summary>Returns a copy of <paramref name="vars"/> with all secret keys' values replaced by <c>***</c>.</summary>
    public static Dictionary<string, string> MaskSecretValues(Dictionary<string, string> vars, HashSet<string> secretKeys)
    {
        if (secretKeys.Count == 0) return vars;
        var masked = new Dictionary<string, string>(vars, StringComparer.OrdinalIgnoreCase);
        foreach (var key in secretKeys)
        {
            if (masked.ContainsKey(key)) masked[key] = "***";
        }
        return masked;
    }

    public static Dictionary<string, string> DeserializeResolvedVariables(string? json)
    {
        if (string.IsNullOrEmpty(json) || json == "{}")
            return [];

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
    }

    public static List<string> DeserializeWarnings(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return [];

        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }

    public static List<CoverageFileDto> DeserializeCoverageFiles(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return [];

        try { return JsonSerializer.Deserialize<List<CoverageFileDto>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    // --- DTO mapping ---

    // Server-side EF projection for the run LIST (GetRunsPagedAsync): scalar run fields + step
    // summary chips only. Deliberately omits the heavy per-run payload (YamlSnapshot, resolved
    // variables, step commands, output variables, artifacts) that GetRunAsync loads - output
    // variables especially must not flow through the list path, which bypasses secret masking.
    public static readonly Expression<Func<PipelineRun, PipelineRunDto>> RunListProjection = r => new PipelineRunDto
    {
        Id = r.Id,
        PipelineId = r.PipelineId,
        ProjectId = r.Pipeline != null ? r.Pipeline.ProjectId : null,
        PipelineName = r.Pipeline != null ? r.Pipeline.Name : string.Empty,
        Status = r.Status,
        StartedAt = r.StartedAt,
        CompletedAt = r.CompletedAt,
        BranchName = r.BranchName,
        CommitHash = r.CommitHash,
        RepositoryUrl = r.Pipeline != null && r.Pipeline.Project != null ? r.Pipeline.Project.RepositoryUrl : null,
        ProjectName = r.Pipeline != null && r.Pipeline.Project != null ? r.Pipeline.Project.Name : null,
        Steps = r.StepRuns.OrderBy(s => s.Order).Select(s => new PipelineStepRunDto
        {
            Id = s.Id,
            StepName = s.StepName,
            StageName = s.StageName,
            Status = s.Status,
            ServerId = s.ServerId,
            ServerName = s.Server != null ? s.Server.Name : null,
            ServerOs = s.Server != null ? s.Server.OsDescription : null,
            StartedAt = s.StartedAt,
            CompletedAt = s.CompletedAt,
            ExitCode = s.ExitCode,
            RetryCount = s.RetryCount,
            ContinueOnError = s.ContinueOnError,
            MatrixLeg = s.MatrixLeg,
            TaskId = s.TaskId,
            IsSystem = s.IsSystem,
            GroupName = s.GroupName,
            TriggeredRunId = s.TriggeredRunId
        }).ToList()
    };

    public static PipelineRunDto MapRunToDto(PipelineRun r) => new()
    {
        Id = r.Id,
        PipelineId = r.PipelineId,
        ProjectId = r.Pipeline?.ProjectId,
        PipelineName = r.Pipeline?.Name ?? string.Empty,
        Status = r.Status,
        StartedAt = r.StartedAt,
        CompletedAt = r.CompletedAt,
        BranchName = r.BranchName,
        CommitHash = r.CommitHash,
        RepositoryUrl = r.Pipeline?.Project?.RepositoryUrl,
        ProjectName = r.Pipeline?.Project?.Name,
        YamlSnapshot = r.YamlSnapshot,
        ResolvedVariables = DeserializeResolvedVariables(r.ResolvedVariablesJson),
        Parameters = DeserializeResolvedVariables(r.ParametersJson),
        Warnings = DeserializeWarnings(r.WarningsJson),
        Steps = r.StepRuns.Select(s => new PipelineStepRunDto
        {
            Id = s.Id,
            StepName = s.StepName,
            StageName = s.StageName,
            Status = s.Status,
            ServerId = s.ServerId,
            ServerName = s.Server?.Name,
            ServerOs = s.Server?.OsDescription,
            StartedAt = s.StartedAt,
            CompletedAt = s.CompletedAt,
            ExitCode = s.ExitCode,
            OutputVariables = DeserializeResolvedVariables(s.OutputVariablesJson),
            RetryCount = s.RetryCount,
            ContinueOnError = s.ContinueOnError,
            MatrixLeg = s.MatrixLeg,
            TaskId = s.TaskId,
            IsSystem = s.IsSystem,
            GroupName = s.GroupName,
            // Command is masked in GetRunAsync (async secret-masking) before reaching the client.
            Command = s.Task?.Command,
            IsContainerIsolated = !string.IsNullOrEmpty(s.Task?.ContainerImage),
            TriggeredRunId = s.TriggeredRunId
        }).ToList(),
        Artifacts = r.Artifacts.Select(a => new PipelineArtifactDto
        {
            Id = a.Id,
            PipelineRunId = a.PipelineRunId,
            PipelineId = a.PipelineId,
            ProjectId = a.ProjectId,
            Name = a.Name,
            FilePath = a.FilePath,
            SizeBytes = a.SizeBytes,
            StageName = a.StageName,
            StepName = a.StepName,
            CreatedAt = a.CreatedAt,
            RetentionPolicy = a.RetentionPolicy,
            RetentionExpiresAt = a.RetentionExpiresAt,
            EnvironmentName = a.EnvironmentName,
            BranchName = r.BranchName,
            CommitHash = r.CommitHash,
            RepositoryUrl = r.Pipeline?.Project?.RepositoryUrl
        }).ToList(),
        TestResultSummary = r.TestResults.Count > 0 ? new PipelineTestResultSummaryDto
        {
            TotalTests = r.TestResults.Count,
            Passed = r.TestResults.Count(t => t.Outcome == TestOutcome.Passed),
            Failed = r.TestResults.Count(t => t.Outcome == TestOutcome.Failed),
            Skipped = r.TestResults.Count(t => t.Outcome == TestOutcome.Skipped),
            Errors = r.TestResults.Count(t => t.Outcome == TestOutcome.Error),
            TotalDurationMs = r.TestResults.Sum(t => t.DurationMs)
        } : null,
        CoverageSummary = r.CoverageResults.Count > 0
            ? CoverageSummaryMapper.Map(CoverageSummaryMapper.SelectCanonical(r.CoverageResults))
            : null,
        LintSummary = r.LintResults.Count > 0 ? new PipelineLintSummaryDto
        {
            Tool = r.LintResults[^1].Tool,
            ErrorCount = r.LintResults[^1].ErrorCount,
            WarningCount = r.LintResults[^1].WarningCount,
            InfoCount = r.LintResults[^1].InfoCount,
            Passed = r.LintResults[^1].ErrorCount == 0
        } : null,
        Metrics = r.RunMetrics.Select(m => new RunMetricDto
        {
            Key = m.Key,
            Type = m.Type,
            Value = m.Value,
            Unit = m.Unit,
            Threshold = m.Threshold,
            StageName = m.StageName,
            StepName = m.StepName
        }).ToList()
    };

    // --- Fail-closed isolation enforcement ---

    public static string? CheckIsolationPolicy(string stageName, PipelineStageDefinition stageDef, Server server)
    {
        var wantsContainer = stageDef.Isolation?.IsContainer == true;

        if (wantsContainer && string.IsNullOrWhiteSpace(stageDef.Isolation?.Image))
            return $"Stage '{stageName}' requests container isolation but no image was given. " +
                   "Add `isolation: { mode: container, image: <image> }`.";

        if (wantsContainer && !server.DockerAvailable)
            return $"Stage '{stageName}' requires container isolation but runner '{server.Name}' has no Docker available. " +
                   "Install Docker on the runner or remove `isolation: container` from the stage.";

        if (server.RequireContainerIsolation && !wantsContainer)
            return $"Stage '{stageName}' must run with container isolation: runner '{server.Name}' enforces a containers-only policy. " +
                   "Add `isolation: { mode: container, image: ... }` to the stage.";

        return null;
    }

    public static string? CheckRunIsolationPolicy(Server server, PipelineIsolationDefinition? runIsolation)
    {
        var wantsContainer = runIsolation?.IsContainer == true;

        if (wantsContainer && string.IsNullOrWhiteSpace(runIsolation?.Image))
            return "Run requests container isolation but no image was given. " +
                   "Add `isolation: { mode: container, image: <image> }`.";

        if (wantsContainer && !server.DockerAvailable)
            return $"Run requires container isolation but runner '{server.Name}' has no Docker available. " +
                   "Install Docker on the runner or remove run-level `isolation: container`.";

        if (server.RequireContainerIsolation && !wantsContainer)
            return $"Runner '{server.Name}' enforces a containers-only policy but the run is not container-isolated. " +
                   "Add run-level `isolation: { mode: container, image: ... }`.";

        return null;
    }

    // --- P-10: Condition Evaluation ---

    public static bool EvaluateCondition(string? condition, Dictionary<string, string> variables, List<string> completedStages, bool previousStageFailed)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;

        var trimmed = condition.Trim().ToLowerInvariant();

        return trimmed switch
        {
            "always()" => true,
            "succeeded()" => !previousStageFailed,
            "failed()" => previousStageFailed,
            "cancelled()" => false,
            _ => EvaluateVariableCondition(condition, variables)
        };
    }

    public static bool EvaluateVariableCondition(string condition, Dictionary<string, string> variables)
    {
        var eqMatch = EqConditionPattern().Match(condition);
        if (eqMatch.Success)
        {
            var varName = eqMatch.Groups[1].Value;
            var expected = eqMatch.Groups[2].Value;
            return variables.TryGetValue(varName, out var actual) &&
                   string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        var neMatch = NeConditionPattern().Match(condition);
        if (neMatch.Success)
        {
            var varName = neMatch.Groups[1].Value;
            var expected = neMatch.Groups[2].Value;
            return !variables.TryGetValue(varName, out var actual) ||
                   !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        var containsMatch = ContainsConditionPattern().Match(condition);
        if (containsMatch.Success)
        {
            var varName = containsMatch.Groups[1].Value;
            var substring = containsMatch.Groups[2].Value;
            return variables.TryGetValue(varName, out var actual) &&
                   actual.Contains(substring, StringComparison.OrdinalIgnoreCase);
        }

        // Unknown condition format - fail-closed (do not run the stage)
        return false;
    }

    // --- P-12: Output Variables ---

    public static Dictionary<string, string> ParseOutputVariablesFromLogs(string? output) => ParseOutputVariables(output);

    private static Dictionary<string, string> ParseOutputVariables(string? output)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(output)) return result;

        var matches = OutputVariablePattern().Matches(output);
        foreach (Match match in matches)
        {
            result[match.Groups[1].Value] = match.Groups[2].Value.Trim();
        }

        return result;
    }

    // S-TECH-56: true when dispatching this stage's job would push its group over MaxParallel.
    public static bool IsGroupThrottled(
        PipelineStageDefinition stageDef, List<PipelineStageDefinition> flattenedStages,
        List<string> activeStageNames, Dictionary<string, int> dispatchedPerGroup)
    {
        if (stageDef.Strategy is not { } strategy || string.IsNullOrEmpty(stageDef.Group))
            return false;

        var maxParallel = Math.Max(1, strategy.MaxParallel);
        var groupJobNames = flattenedStages
            .Where(s => string.Equals(s.Group, stageDef.Group, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inFlight = activeStageNames.Count(groupJobNames.Contains);
        var dispatched = dispatchedPerGroup.GetValueOrDefault(stageDef.Group);
        return inFlight + dispatched >= maxParallel;
    }

    // --- P-24: Matrix Expansion ---

    // S2 (audit 2026-07-06): matrix values become $(axis)-substitutable text in step shell scripts
    // (PipelineCommandBuilder.Substitute splices values verbatim - the same channel as WEBHOOK_REF),
    // so constrain them to their legitimate shape at run creation. Same discipline as
    // PipelineWebhookService: \A...\z true-end anchoring (no trailing-newline bypass) and a first
    // char excluding '-' (argument-injection token). No space either - matching the webhook-ref
    // discipline, a space is the shell's argument separator so an unquoted $(axis) splice would
    // word-split. Matrix axes name OS/framework/config values ("ubuntu-22.04", "net8.0",
    // "linux/amd64", "node:20") - they never need a space or a shell metacharacter.
    // F-ENG-02/03: the tail also allows '=' (key=value), '%' (50%), ',', '^' and '~' (semver ranges) - all
    // shell-inert in a bare word. STILL rejected, by design: a leading '-' (argument-injection token), '\'
    // (shell escape, so Windows-style paths must use '/'), spaces and every shell metacharacter.
    [GeneratedRegex(@"\A[A-Za-z0-9._+][A-Za-z0-9._+/:@=%,~^-]{0,199}\z")]
    private static partial Regex SafeMatrixValueRegex();

    /// <summary>Rejects matrix values whose shape could break out of the shell template once
    /// spliced by <c>Substitute</c>. Returns one error per offending value (empty = valid).</summary>
    public static List<string> ValidateMatrixValues(IEnumerable<PipelineStageDefinition> stages)
    {
        var errors = new List<string>();
        foreach (var stage in stages)
        {
            if (stage.Matrix is not { Count: > 0 }) continue;
            if (stage.Matrix.Count > MaxMatrixAxes)
                errors.Add($"Stage '{stage.Name}': matrix has {stage.Matrix.Count} axes; the maximum is {MaxMatrixAxes}.");

            long legCount = 1;
            foreach (var (axis, values) in stage.Matrix)
            {
                if (values is null || values.Count == 0)
                {
                    errors.Add($"Stage '{stage.Name}': matrix axis '{axis}' must contain at least one value.");
                    legCount = 0;
                    continue;
                }
                if (values.Count > MaxMatrixValuesPerAxis)
                    errors.Add($"Stage '{stage.Name}': matrix axis '{axis}' has {values.Count} values; the maximum is {MaxMatrixValuesPerAxis}.");
                legCount = Math.Min((long)MaxMatrixLegs + 1, legCount * values.Count);
                foreach (var value in values)
                {
                    if (value is null || !SafeMatrixValueRegex().IsMatch(value))
                        errors.Add($"Stage '{stage.Name}': matrix value '{value}' for axis '{axis}' contains characters outside the allowed shape (letters, digits, '._+/:@=%,~^-', no leading '-', no backslash or space).");
                }
            }
            if (legCount > MaxMatrixLegs)
                errors.Add($"Stage '{stage.Name}': matrix expands beyond the maximum of {MaxMatrixLegs} legs.");
        }
        return errors;
    }

    public static List<string> ValidateExecutionRoles(IEnumerable<PipelineStageDefinition> stages)
    {
        var errors = new List<string>();
        foreach (var stage in stages)
        {
            if (string.IsNullOrWhiteSpace(stage.ExecutionRole)) continue;
            if (!stage.ExecutionRole.Equals("build", StringComparison.OrdinalIgnoreCase)
                && !stage.ExecutionRole.Equals("deploy", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Stage '{stage.Name}': execution_role must be 'build' or 'deploy', not '{stage.ExecutionRole}'.");
            }
        }
        return errors;
    }

    public static List<Dictionary<string, string>> ExpandMatrix(Dictionary<string, List<string>>? matrix)
    {
        if (matrix is null || matrix.Count == 0)
            return [new Dictionary<string, string>()]; // Single empty leg for non-matrix stages

        var cardinalityErrors = ValidateMatrixValues(
            [new PipelineStageDefinition { Name = "matrix", Matrix = matrix }]);
        if (cardinalityErrors.Count > 0)
            throw new ArgumentException(string.Join(" ", cardinalityErrors), nameof(matrix));

        var keys = matrix.Keys.ToList();
        var result = new List<Dictionary<string, string>> { new() };

        foreach (var key in keys)
        {
            var values = matrix[key];
            var expanded = new List<Dictionary<string, string>>();
            foreach (var existing in result)
            {
                foreach (var value in values)
                {
                    var newLeg = new Dictionary<string, string>(existing) { [key] = value };
                    expanded.Add(newLeg);
                }
            }
            result = expanded;
        }

        return result;
    }

    public static List<string> BuildDeadlockReasons(
        List<PipelineStepRun> pendingSteps, PipelineYamlDefinition definition, List<string> completedStages)
    {
        var reasons = new List<string>();
        foreach (var stageName in pendingSteps.Select(s => s.StageName).Distinct())
        {
            if (stageName is PipelineRunService.SystemPrepareStage or PipelineRunService.SystemCleanupStage) continue;
            var stageDef = YamlParsingHelper.FlattenJobs(definition).FirstOrDefault(s => s.Name == stageName);
            if (stageDef is null)
            {
                reasons.Add($"Stage '{stageName}' is not defined in the pipeline and cannot run.");
                continue;
            }

            var unmet = stageDef.DependsOn.Where(d => !completedStages.Contains(d)).ToList();
            if (unmet.Count > 0)
                reasons.Add(
                    $"Stage '{stageName}' is blocked: it depends on " +
                    $"{string.Join(", ", unmet.Select(u => $"'{u}'"))} which did not complete successfully.");
        }

        if (reasons.Count == 0)
            reasons.Add("Pipeline is deadlocked: pending stages cannot be scheduled.");

        return reasons;
    }

    public static (PreflightTargetKind Kind, string Label) DescribeTarget(PipelineStageDefinition stage)
    {
        if (!string.IsNullOrEmpty(stage.Pool))
            return (PreflightTargetKind.Pool, stage.Pool);
        if (!string.IsNullOrEmpty(stage.Environment))
            return (PreflightTargetKind.Environment, stage.Environment);
        return (PreflightTargetKind.Agent, string.IsNullOrEmpty(stage.Agent) ? "(unspecified)" : stage.Agent);
    }

    // --- Step env scoping / dispatch / container isolation (from the task-creation path) ---

    public static Dictionary<string, string> ScopeStepEnvironment(
        Dictionary<string, string> legVars, HashSet<string> secretKeys, PipelineStepDefinition stepDef)
    {
        // S-TECH-V6QN: drop dotted keys (e.g. the "parameters.X" namespace) from the exported env.
        if (legVars.Keys.Any(k => k.Contains('.', StringComparison.Ordinal)))
            legVars = legVars.Where(kv => !kv.Key.Contains('.', StringComparison.Ordinal))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        if (secretKeys.Count == 0) return legVars;
        var referenceText = stepDef.Shell + "\n" + (stepDef.WorkingDirectory ?? string.Empty);
        // A `checkout: true` step runs the auto-injected git-clone preamble, which references the
        // git credential vars even though the user's shell text never mentions them.
        if (stepDef.Checkout)
            referenceText += "\nGIT_USERNAME\nGIT_PASSWORD";
        var scoped = new Dictionary<string, string>(legVars, StringComparer.OrdinalIgnoreCase);
        foreach (var key in secretKeys)
        {
            if (scoped.ContainsKey(key) && !referenceText.Contains(key, StringComparison.OrdinalIgnoreCase))
                scoped.Remove(key);
        }
        return scoped;
    }

    public static void MarkStepDispatched(PipelineStepRun stepRun, ServerTask task)
    {
        stepRun.Task = task;
        stepRun.Status = TaskExecutionStatus.Assigned;
    }

    /// <summary>Applies container isolation to the task and returns a (possibly empty) list of warnings for
    /// limits that were dropped because they were malformed - so a rejected cap is surfaced, not fail-open.</summary>
    public static IReadOnlyList<string> ApplyContainerIsolation(ServerTask task, PipelineIsolationDefinition? isolation)
    {
        if (isolation?.IsContainer != true) return [];

        // Defense in depth: save and run preparation reject malformed caps. If an internal caller ever
        // bypasses those gates, dispatch still fails instead of silently running an uncapped container.
        var limitErrors = ValidateIsolationLimits(
            [new PipelineStageDefinition { Name = task.Name, Isolation = isolation }]);
        if (limitErrors.Count > 0)
            throw new ArgumentException(string.Join(" ", limitErrors), nameof(isolation));

        task.Executor = ExecutorType.Container;
        task.ContainerImage = isolation.Image;
        task.ContainerRuntime = isolation.Runtime;
        task.ContainerNetwork = isolation.Network;
        if (IsValidMemory(isolation.Memory))
            task.ContainerMemory = isolation.Memory;

        if (IsValidCpus(isolation.Cpus))
            task.ContainerCpus = isolation.Cpus;

        return [];
    }

    public static List<string> ValidateIsolationLimits(IEnumerable<PipelineStageDefinition> stages)
    {
        var errors = new List<string>();
        foreach (var stage in stages)
        {
            var isolation = stage.Isolation;
            if (isolation is null) continue;
            if (!string.IsNullOrWhiteSpace(isolation.Memory) && !IsValidMemory(isolation.Memory))
                errors.Add($"Stage '{stage.Name}': container memory limit '{isolation.Memory}' is invalid.");
            if (!string.IsNullOrWhiteSpace(isolation.Cpus) && !IsValidCpus(isolation.Cpus))
                errors.Add($"Stage '{stage.Name}': container CPU limit '{isolation.Cpus}' is invalid.");
        }
        return errors;
    }

    private static bool IsValidMemory(string? value) =>
        !string.IsNullOrWhiteSpace(value) && MemoryLimitPattern().IsMatch(value.Trim());

    private static bool IsValidCpus(string? value) =>
        !string.IsNullOrWhiteSpace(value) && CpusLimitPattern().IsMatch(value.Trim());

    public static Dictionary<string, string> ResolveLegVariables(
        Dictionary<string, string> stageVars, string legKey, List<Dictionary<string, string>> matrixLegs)
    {
        var legVars = new Dictionary<string, string>(stageVars, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(legKey)) return legVars;

        var matchingLeg = matrixLegs.FirstOrDefault(l => string.Join("-", l.Values) == legKey);
        if (matchingLeg is not null)
        {
            foreach (var (key, value) in matchingLeg)
                legVars[key] = value;
        }
        return legVars;
    }
}
