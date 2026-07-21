// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.ExternalRepos;
using Aetheus.Back.Components.VariableLibraries;
using Aetheus.Back.Components.Vaults;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Helpers;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// F-009: extracted from <see cref="PipelineRunService"/> to reduce its dependency count and
/// improve cohesion. Resolves all pipeline variables (system, inline, libraries, vaults,
/// additional) and tracks warnings + secret-key set.
/// </summary>
internal sealed class PipelineVariableResolver(
    IVariableLibraryService variableLibraryService,
    IVaultService vaultService,
    IPipelineRepository repo,
    IConfiguration configuration,
    TimeProvider timeProvider) : IPipelineVariableResolver
{
    public async Task<(Dictionary<string, string> Resolved, List<string> Warnings, HashSet<string> SecretKeys)> ResolveVariablesWithWarningsAsync(
        PipelineYamlDefinition definition, int? projectId, Dictionary<string, string>? additionalVariables, CancellationToken ct,
        int? pipelineId = null, int? runId = null, string? pipelineName = null)
    {
        var warnings = new List<string>();
        var secretKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BUILD_BUILDID"] = (runId ?? 0).ToString(),
            ["BUILD_BUILDNUMBER"] = (runId ?? 0).ToString(),
            ["BUILD_PIPELINEID"] = (pipelineId ?? 0).ToString(),
            ["BUILD_PIPELINENAME"] = pipelineName ?? definition.Name,
            ["BUILD_TRIGGEREDBY"] = definition.Trigger,
            ["SYSTEM_DATE"] = now.ToString("yyyy-MM-dd"),
            ["SYSTEM_DATETIME"] = now.ToString("o"),
            ["SYSTEM_TIMESTAMP"] = new DateTimeOffset(now, TimeSpan.Zero).ToUnixTimeSeconds().ToString(),
            ["AETHEUS_VERSION"] = typeof(PipelineVariableResolver).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            ["CI"] = "true",
        };

        // S-TECH-53: expose the declared project type so step commands/conditions can auto-adapt.
        if (!string.IsNullOrWhiteSpace(definition.ProjectType))
            resolved["PROJECT_TYPE"] = definition.ProjectType;

        if (projectId is { } pid0)
        {
            resolved["BUILD_PROJECTID"] = pid0.ToString();
            resolved["WORKSPACE"] = PipelineCommandBuilder.GetDefaultWorkspace(runId ?? 0, isWindows: false);
        }

        // P: parameter defaults are the lowest-precedence layer - TryAdd so an explicit YAML variable,
        // library, vault, or queue-time value of the same name overrides them. The user-supplied
        // parameter values ride in additionalVariables (highest precedence, applied below).
        // Defaults() is the single source of truth for "what defaults a pipeline declares".
        foreach (var (name, value) in PipelineParameterResolver.Defaults(definition.Parameters))
            resolved.TryAdd(name, value);

        foreach (var (key, value) in definition.Variables)
            resolved[key] = value;

        if (definition.VariableLibraries.Count > 0)
        {
            var (libraryVars, foundLibNames) = projectId is { } pid
                ? await variableLibraryService.ResolveLibrariesWithCrossAccessAndNamesAsync(definition.VariableLibraries, pid, ct).ConfigureAwait(false)
                : await variableLibraryService.ResolveLibrariesWithNamesAsync(definition.VariableLibraries, projectId, ct).ConfigureAwait(false);

            foreach (var name in definition.VariableLibraries)
            {
                if (!foundLibNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    warnings.Add($"Variable library '{name}' not found.");
            }

            foreach (var (key, value) in libraryVars)
                resolved[key] = value;
        }

        if (definition.Vaults.Count > 0)
        {
            var (vaultVars, foundVaultNames) = projectId is { } pid2
                ? await vaultService.ResolveVaultSecretsWithCrossAccessAndNamesAsync(definition.Vaults, pid2, ct).ConfigureAwait(false)
                : await vaultService.ResolveVaultSecretsWithNamesAsync(definition.Vaults, projectId, ct).ConfigureAwait(false);

            foreach (var name in definition.Vaults)
            {
                if (!foundVaultNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    warnings.Add($"Vault '{name}' not found.");
            }

            foreach (var (key, value) in vaultVars)
            {
                resolved[key] = value;
                secretKeys.Add(key);
            }
        }

        if (additionalVariables is { Count: > 0 })
        {
            foreach (var (key, value) in additionalVariables)
                resolved[key] = value;
        }

        for (var pass = 0; pass < 3; pass++)
        {
            var anyReplaced = false;
            foreach (var key in resolved.Keys.ToList())
            {
                var val = resolved[key];
                if (!val.Contains("$(")) continue;
                var newVal = PipelineCommandBuilder.Substitute(val, resolved);
                if (newVal != val)
                {
                    resolved[key] = newVal;
                    anyReplaced = true;
                }
            }
            if (!anyReplaced) break;
        }

        PipelineDeploymentTargetGuard.ValidateVariables(resolved);

        return (resolved, warnings, secretKeys);
    }

    public static void InjectStageSystemVariables(
        Dictionary<string, string> stageVars, string stageName, Server server)
    {
        stageVars["SYSTEM_STAGENAME"] = stageName;
        stageVars["AGENT_NAME"] = server.Name;
        stageVars["AGENT_HOSTNAME"] = server.Hostname;
        stageVars["AGENT_OS"] = server.OsDescription ?? string.Empty;
        stageVars["AGENT_PLATFORM"] = OsTypeHelper.IsWindows(server.OsType, server.OsDescription) ? "Windows" : "Linux";
        stageVars["AGENT_ID"] = server.Id.ToString();
    }

    public async Task<Dictionary<string, string>> ResolveFullVariablesForRunAsync(
        PipelineRun run, PipelineYamlDefinition definition, CancellationToken ct)
        => (await ResolveFullVariablesWithSecretsForRunAsync(run, definition, ct).ConfigureAwait(false)).Resolved;

    public async Task<(Dictionary<string, string> Resolved, HashSet<string> SecretKeys)> ResolveFullVariablesWithSecretsForRunAsync(
        PipelineRun run, PipelineYamlDefinition definition, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(run.Pipeline, nameof(run.Pipeline));

        var additionalVars = PipelineRunService.DeserializeResolvedVariablesStatic(run.AdditionalVariablesJson);
        var projectId = run.Pipeline.ProjectId
            ?? await repo.GetPipelineProjectIdAsync(run.Pipeline, ct).ConfigureAwait(false);

        // A run is resolved again before every stage. Its immutable source revision must therefore be
        // re-injected here, not only at launch: otherwise BUILD_SOURCEVERSION disappears after Prepare
        // and CI can package an unpinned artifact despite the run itself having a valid commit.
        if (PipelineRunService.IsGitCommitHash(run.CommitHash))
            additionalVars["BUILD_SOURCEVERSION"] = run.CommitHash!;

        // S-TECH-RPVI: shared injection point (see PipelineProjectVariables), identical to the
        // trigger-time path so the rehomed mirror URL stays consistent across re-resolutions.
        PipelineProjectVariables.Inject(
            additionalVars, run.Pipeline?.Project, configuration, run.BranchName ?? run.Pipeline?.SourceBranch);

        var (resolved, _, secretKeys) = await ResolveVariablesWithWarningsAsync(
            definition, projectId, additionalVars, ct,
            pipelineId: run.PipelineId, runId: run.Id,
            pipelineName: run.Pipeline?.Name).ConfigureAwait(false);
        return (resolved, secretKeys);
    }
}
